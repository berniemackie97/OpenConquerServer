using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Time;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Maps;

/// <summary>
/// Completes the existing-character EnterMap exchange using the previously resolved map content.
/// </summary>
internal sealed class ExistingCharacterEnterMapProcessor(IGameTickSource tickSource)
{
    private readonly IGameTickSource _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));

    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        ExistingCharacterGameConnection connection = login.Connection;
        GameAction request = await LoginActionRequest.ReadAfterMissionCatalogAsync(login, GameAction.EnterMapAction, "EnterMap request", cancellationToken)
            .ConfigureAwait(false);

        if (request.EntityId != login.CharacterId)
        {
            throw new InvalidDataException("The EnterMap request character ID does not match the authenticated character.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        WorldMapContent map = login.Map;
        GameMapInfoPacket mapInfo = new(map.MapId, map.MapDataId, map.Flags);
        GameWeatherUpdatePacket weather = GameWeatherUpdatePacket.CreateClear();
        GameActionPacket acknowledgement = GameActionPacket.CreateEnterMapAcknowledgement(login.CharacterId, map.MapDataId,
            login.Profile.Location.X, login.Profile.Location.Y, _tickSource.CurrentTick);

        await connection.WriteAsync(mapInfo, cancellationToken).ConfigureAwait(false);
        await connection.WriteAsync(weather, cancellationToken).ConfigureAwait(false);
        await connection.WriteAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
    }
}
