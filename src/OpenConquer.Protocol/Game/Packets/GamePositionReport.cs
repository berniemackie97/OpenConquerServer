using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// One native 5517 client-to-server position statement (MsgAction 10010, subtype 100 or 146).
/// A report is telemetry, not authorization to move an entity.
/// </summary>
public readonly record struct GamePositionReport(uint EntityId, ushort X, ushort Y, ushort Action)
{
    public const ushort IdleReportAction = 100;
    public const ushort CorrectionReplyAction = 146;

    public static bool TryParse(GameInboundFrame frame, out GamePositionReport report)
    {
        ArgumentNullException.ThrowIfNull(frame);
        report = default;

        if (frame.Header.Length != GameAction.FixedPacketLength ||
            !GameAction.TryParse(frame, out GameAction action, out _) ||
            action.Action is not (IdleReportAction or CorrectionReplyAction) ||
            action.EntityId == 0 || action.StringCount != 0)
        {
            return false;
        }

        // The native client puts its reported (X,Y) pair in the dword at wire +8.
        // PositionX/PositionY at +24/+26 belong to the inverse correction packet.
        report = new GamePositionReport(action.EntityId,
            unchecked((ushort)action.ParameterPair),
            unchecked((ushort)(action.ParameterPair >> 16)), action.Action);
        return true;
    }
}
