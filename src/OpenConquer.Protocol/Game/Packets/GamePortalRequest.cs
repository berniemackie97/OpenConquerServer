using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>The retail 5517 portal request carries an exit marker tile, not a passway index.</summary>
public readonly record struct GamePortalRequest(uint EntityId, ushort CandidateX, ushort CandidateY)
{
    public static bool TryParse(GameInboundFrame frame, out GamePortalRequest request)
    {
        request = default;
        if (frame is null || frame.Header.Length != GameAction.FixedPacketLength ||
            !GameAction.TryParse(frame, out GameAction action, out _) ||
            action.Action != GameAction.PortalRequestAction || action.StringCount != 0)
        {
            return false;
        }

        request = new GamePortalRequest(action.EntityId, (ushort)action.ParameterPair,
            (ushort)(action.ParameterPair >> 16));
        return true;
    }
}
