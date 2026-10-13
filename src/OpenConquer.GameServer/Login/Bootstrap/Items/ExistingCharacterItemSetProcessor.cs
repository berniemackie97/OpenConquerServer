using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Items;

internal sealed class ExistingCharacterItemSetProcessor(ICharacterItemSetResolver resolver, TimeProvider timeProvider)
{
    private readonly ICharacterItemSetResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAsync(connection, GameAction.GetItemSetAction, "item-set request", cancellationToken).ConfigureAwait(false);

        LoginActionRequest.RequireCharacterRequest(request, login.CharacterId, "item-set request");

        DateTimeOffset utcNow = _timeProvider.GetUtcNow();

        cancellationToken.ThrowIfCancellationRequested();

        CharacterItemSet resolvedItemSet = await _resolver.ResolveAsync(login.CharacterId, utcNow, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (resolvedItemSet.CharacterId != login.CharacterId)
        {
            throw new InvalidDataException($"The resolved item set belongs to character {resolvedItemSet.CharacterId}, not authenticated character {login.CharacterId}.");
        }

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(resolvedItemSet, utcNow);

        cancellationToken.ThrowIfCancellationRequested();

        GameActionPacket acknowledgement = new(login.CharacterId, 0, 0, request.Timestamp, GameAction.GetItemSetAction, 0, 0, 0, 0, 0, 0);

        foreach (GameLocalItemSnapshotPacket itemSnapshot in projection.ItemSnapshots)
        {
            await connection.WriteAsync(itemSnapshot, cancellationToken).ConfigureAwait(false);
        }

        if (projection.ActiveEquipmentSnapshot is { } activeEquipmentSnapshot)
        {
            await connection.WriteAsync(activeEquipmentSnapshot, cancellationToken).ConfigureAwait(false);
        }

        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);

        login.ItemSet = projection.RuntimeItemSet;
    }
}
