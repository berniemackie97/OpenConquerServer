using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterItemSetProcessor(ICharacterItemSetResolver resolver, TimeProvider timeProvider)
{
    private readonly ICharacterItemSetResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Takes ownership of <paramref name="awaitingItemSet"/> and transfers its connection only after the native item-set request is validated and the complete item state is sent.
    /// </summary>
    public async ValueTask<AwaitingFriendListConnection> ProcessAsync(AwaitingItemSetConnection awaitingItemSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awaitingItemSet);

        ExistingCharacterGameConnection connection = awaitingItemSet.TakeConnection();
        CharacterLoginProfile profile = awaitingItemSet.Profile;
        GameMapEntryDefinition map = awaitingItemSet.Map;
        using CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.RevocationToken);
        CancellationToken operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();

            GameAction10010 itemSetRequest;

            using (GameInboundFrame? frame = await connection.ReadAsync(operationToken).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    throw new EndOfStreamException("The GameServer connection closed while awaiting the item-set request.");
                }

                if (!GameAction10010.TryParse(frame, out itemSetRequest, out GameActionParseError parseError))
                {
                    throw new InvalidDataException($"The post-map-state GameServer frame is not a valid MsgAction 10010: {parseError}.");
                }

                if (frame.Header.Length != GameAction10010.FixedPacketLength || itemSetRequest.StringCount != 0)
                {
                    throw new InvalidDataException("The native item-set request must contain the fixed MsgAction body without trailing strings.");
                }
            }

            if (itemSetRequest.Action != GameAction10010.GetItemSetAction)
            {
                throw new InvalidDataException($"Expected item-set action {GameAction10010.GetItemSetAction}, received action {itemSetRequest.Action}.");
            }

            if (itemSetRequest.EntityId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException("The item-set request character ID does not match the authenticated character.");
            }

            if (itemSetRequest.ParameterPair != 0 || itemSetRequest.ActionParameter != 0 || itemSetRequest.Direction != 0
                || itemSetRequest.PositionX != 0 || itemSetRequest.PositionY != 0 || itemSetRequest.Data1 != 0
                || itemSetRequest.Data2 != 0 || itemSetRequest.Flag != 0)
            {
                throw new InvalidDataException("The native item-set request requires all non-identity, non-timestamp and non-action MsgAction fields to be zero.");
            }

            DateTimeOffset utcNow = _timeProvider.GetUtcNow();

            operationToken.ThrowIfCancellationRequested();

            CharacterItemSet resolvedItemSet = await _resolver.ResolveAsync(profile.Identity.CharacterId, utcNow, operationToken).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (resolvedItemSet.CharacterId != profile.Identity.CharacterId)
            {
                throw new InvalidDataException($"The resolved item set belongs to character {resolvedItemSet.CharacterId}, not authenticated character {profile.Identity.CharacterId}.");
            }

            ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(resolvedItemSet, utcNow);

            operationToken.ThrowIfCancellationRequested();

            GameActionPacket10010 acknowledgement = new(profile.Identity.CharacterId, 0, 0, itemSetRequest.Timestamp,
                GameAction10010.GetItemSetAction, 0, 0, 0, 0, 0, 0);

            foreach (GameLocalItemSnapshotPacket1008 itemSnapshot in projection.ItemSnapshots)
            {
                await connection.WriteAsync(itemSnapshot, operationToken).ConfigureAwait(false);
            }

            if (projection.ActiveEquipmentSnapshot is { } activeEquipmentSnapshot)
            {
                await connection.WriteAsync(activeEquipmentSnapshot, operationToken).ConfigureAwait(false);
            }

            await connection.WriteAsync(acknowledgement, operationToken).ConfigureAwait(false);

            return new AwaitingFriendListConnection(connection, map, projection.RuntimeItemSet);
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

        return new AggregateException("Existing-character item-set processing failed and connection cleanup also failed.", failures);
    }
}
