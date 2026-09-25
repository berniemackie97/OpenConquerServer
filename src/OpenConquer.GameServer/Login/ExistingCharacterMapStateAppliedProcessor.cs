using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login;

internal sealed class ExistingCharacterMapStateAppliedProcessor
{
    /// <summary>
    /// Takes ownership of <paramref name="enteredMap"/> and transfers its connection only after the native client-state-applied notification is validated.
    /// </summary>
    public async ValueTask<AwaitingItemSetConnection> ProcessAsync(EnteredMapConnection enteredMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enteredMap);

        AuthenticatedGameConnection connection = enteredMap.TakeConnection();
        CharacterLoginProfile profile = enteredMap.Profile;
        GameMapEntryDefinition map = enteredMap.Map;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameAction10010 clientStateApplied;

            using (GameInboundFrame? frame = await connection.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the client-state-applied notification.");
                }

                if (!GameAction10010.TryParse(frame, out clientStateApplied, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-EnterMap GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || clientStateApplied.StringCount != 0)
                {
                    throw new InvalidDataException("The native client-state-applied notification must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (clientStateApplied.Action != GameAction10010.ClientStateAppliedAction)
            {
                throw new InvalidDataException($"Expected client-state-applied action {GameAction10010.ClientStateAppliedAction}, received action {clientStateApplied.Action}.");
            }

            if (clientStateApplied.EntityId != 0 || clientStateApplied.ParameterPair != 0 || clientStateApplied.ActionParameter != 0
                || clientStateApplied.Timestamp != 0 || clientStateApplied.Direction != 0 || clientStateApplied.PositionX != 0
                || clientStateApplied.PositionY != 0 || clientStateApplied.Data1 != 0 || clientStateApplied.Data2 != 0
                || clientStateApplied.Flag != 0)
            {
                throw new InvalidDataException("The native client-state-applied notification requires all non-action MsgAction fields to be zero.");
            }

            return new AwaitingItemSetConnection(connection, profile, map);
        }
        catch (Exception processingException)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw CreateProcessingFailure(processingException, cleanupException);
            }

            throw;
        }
    }

    private static AggregateException CreateProcessingFailure(Exception processingException, Exception cleanupException)
    {
        List<Exception> failures = [processingException];

        if (cleanupException is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupException);
        }

        return new AggregateException("Existing-character client-state-applied processing failed and connection cleanup also failed.", failures);
    }
}
