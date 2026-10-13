using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.Bootstrap.Maps;

internal sealed class ExistingCharacterMapStateAppliedProcessor
{
    public async ValueTask ProcessAsync(ExistingCharacterLoginContext login, CancellationToken cancellationToken = default)
    {
        GameAction notification = await LoginActionRequest.ReadAsync(login.Connection, GameAction.ClientStateAppliedAction,
            "client-state-applied notification", cancellationToken).ConfigureAwait(false);

        if (notification.EntityId != 0 || notification.ParameterPair != 0 || notification.ActionParameter != 0
            || notification.Timestamp != 0 || notification.Direction != 0 || notification.PositionX != 0
            || notification.PositionY != 0 || notification.Data1 != 0 || notification.Data2 != 0
            || notification.Flag != 0)
        {
            throw new InvalidDataException("The client-state-applied notification requires all non-action fields to be zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
