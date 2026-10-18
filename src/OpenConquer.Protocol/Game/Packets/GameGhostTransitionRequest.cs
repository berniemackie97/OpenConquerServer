using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// 5517 client request 10010/145, sent once after the corpse delay.
/// </summary>
public readonly record struct GameGhostTransitionRequest(uint EntityId)
{
    public const ushort Action = 145;

    public static bool TryParse(GameInboundFrame frame, out GameGhostTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(frame);
        request = default;
        if (frame.Header.Length != GameAction.FixedPacketLength ||
            !GameAction.TryParse(frame, out GameAction action, out _) ||
            action.Action != Action || action.EntityId == 0 ||
            action.ParameterPair != 0 || action.ActionParameter != 0 ||
            action.Direction != 0 || action.PositionX != 0 || action.PositionY != 0 ||
            action.Data1 != 0 || action.Data2 != 0 || action.Flag != 0 || action.StringCount != 0)
        {
            return false;
        }

        request = new GameGhostTransitionRequest(action.EntityId);
        return true;
    }
}
