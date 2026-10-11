using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Completes the existing-character EnterMap exchange using previously resolved canonical world content.
/// </summary>
internal sealed class ExistingCharacterEnterMapProcessor(IGameTickSource tickSource)
{
    private readonly IGameTickSource _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));

    /// <summary>
    /// Takes ownership of the awaiting connection and transfers it only after the complete EnterMap exchange succeeds.
    /// </summary>
    public async ValueTask<EnteredMapConnection> ProcessAsync(AwaitingEnterMapConnection awaitingEnterMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingEnterMap);

        ExistingCharacterGameConnection connection = awaitingEnterMap.TakeConnection();
        CharacterLoginProfile profile = awaitingEnterMap.Profile;
        WorldMapContent map = awaitingEnterMap.Map;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            if (profile.Location.MapId != map.MapId)
            {
                throw new InvalidDataException("The resolved EnterMap content does not match the character's persisted map.");
            }

            GameAction10010 enterMapRequest;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the EnterMap request.");
                }

                if (!GameAction10010.TryParse(frame, out enterMapRequest, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-bootstrap GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || enterMapRequest.StringCount != 0)
                {
                    throw new InvalidDataException("The EnterMap request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (enterMapRequest.Action != GameAction10010.EnterMapAction)
            {
                throw new InvalidDataException($"Expected EnterMap action {GameAction10010.EnterMapAction}, received action {enterMapRequest.Action}.");
            }

            if (enterMapRequest.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The EnterMap request character ID does not match the authenticated character.");
            }

            operationToken.ThrowIfCancellationRequested();

            GameMapInfoPacket1110 mapInfo = new(map.MapId, map.MapDataId, map.Flags);
            GameWeatherUpdatePacket1016 weather = GameWeatherUpdatePacket1016.CreateClear();
            GameActionPacket10010 acknowledgement = GameActionPacket10010.CreateEnterMapAcknowledgement(profile.Identity.CharacterId, map.MapDataId, profile.Location.X, profile.Location.Y, _tickSource.CurrentTick);

            await connection.WriteAsync(mapInfo, operationToken).ConfigureAwait(false);
            await connection.WriteAsync(weather, operationToken).ConfigureAwait(false);
            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new EnteredMapConnection(connection, map);
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

        return new AggregateException("Existing-character EnterMap processing failed and connection cleanup also failed.", failures);
    }
}
