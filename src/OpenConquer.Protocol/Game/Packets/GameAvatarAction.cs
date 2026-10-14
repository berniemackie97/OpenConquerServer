using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// A native 5517 fixed-layout change-direction (79) or character-pose (81) request.
/// Only the authenticated character's presentation state may change, never world coordinates.
/// </summary>
public readonly record struct GameAvatarAction(uint EntityId, ushort Kind, byte Direction, ushort Pose, uint Timestamp)
{
    public const ushort DirectionAction = 79;
    public const ushort PoseAction = 81;
    public const ushort StandingPose = 100;

    public static bool TryParse(GameInboundFrame frame, out GameAvatarAction request)
    {
        ArgumentNullException.ThrowIfNull(frame);
        request = default;

        if (frame.Header.Length != GameAction.FixedPacketLength ||
            !GameAction.TryParse(frame, out GameAction action, out _) ||
            action.StringCount != 0 || action.EntityId == 0)
        {
            return false;
        }

        if (action.Action == DirectionAction)
        {
            if (action.Direction >= 8)
            {
                return false;
            }

            request = new GameAvatarAction(action.EntityId, DirectionAction, (byte)action.Direction, 0, action.Timestamp);
            return true;
        }

        if (action.Action != PoseAction || action.ParameterPair > ushort.MaxValue ||
            !IsSupportedPose((ushort)action.ParameterPair))
        {
            return false;
        }

        request = new GameAvatarAction(action.EntityId, PoseAction, 0, (ushort)action.ParameterPair, action.Timestamp);
        return true;
    }

    public GameActionPacket CreateAcknowledgement() => new(EntityId,
        parameterPair: Kind == PoseAction ? (uint)Pose : 0u, actionParameter: 0,
        timestamp: Timestamp, action: Kind, direction: Kind == DirectionAction ? Direction : (ushort)0,
        positionX: 0, positionY: 0, data1: 0, data2: 0, flag: 0);

    /// <summary>Confirmed 5517-era EntityAction values, not an arbitrary user-chosen animation ID.</summary>
    public static bool IsSupportedPose(ushort pose) => pose is
        (>= 1 and <= 8) or 100 or 150 or 160 or 170 or 190 or 200 or 210 or 230 or 250 or 270;
}
