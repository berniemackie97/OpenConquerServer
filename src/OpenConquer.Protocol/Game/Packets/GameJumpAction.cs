using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>Native 5517 MsgAction (10010), subtype 137: absolute-tile jump intent.</summary>
/// <remarks>The packed target at wire +8 is not a relative movement delta.</remarks>
public readonly record struct GameJumpAction(GameAction Action)
{
    public const ushort JumpAction = 137;

    public uint EntityId => Action.EntityId;
    public ushort TargetX => unchecked((ushort)Action.ParameterPair);
    public ushort TargetY => unchecked((ushort)(Action.ParameterPair >> 16));
    public uint Timestamp => Action.Timestamp;
    // Wire +36 selects mounted charge (1) versus ordinary ground jump (0).
    public bool IsGroundJump => Action.Flag == 0;
    public bool IsMountedCharge => Action.Flag == 1;

    public static bool TryParse(GameInboundFrame frame, out GameJumpAction request)
    {
        ArgumentNullException.ThrowIfNull(frame);
        request = default;
        if (frame.Header.Length != GameAction.FixedPacketLength ||
            !GameAction.TryParse(frame, out GameAction action, out _) ||
            action.Action != JumpAction || action.EntityId == 0 || action.StringCount != 0)
        {
            return false;
        }

        request = new GameJumpAction(action);
        return true;
    }

    /// <summary>All validated fixed-layout fields are preserved on the post-commit echo.</summary>
    public GameActionPacket CreateAcknowledgement() => new(Action.EntityId, Action.ParameterPair,
        Action.ActionParameter, Action.Timestamp, Action.Action, Action.Direction,
        Action.PositionX, Action.PositionY, Action.Data1, Action.Data2, Action.Flag);
}
