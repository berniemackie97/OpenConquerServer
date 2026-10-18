using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// A 5517 position report from the client (MsgAction 10010, subtype 100 or 146). It is only a report and
/// never moves anything.
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
