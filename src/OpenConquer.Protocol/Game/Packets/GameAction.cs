using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Fixed body of the action packet (10010), the general-purpose request/acknowledgement message.
/// </summary>
public readonly record struct GameAction(uint EntityId, uint ParameterPair, uint ActionParameter, uint Timestamp, ushort Action, ushort Direction, ushort PositionX, ushort PositionY, uint Data1, uint Data2, byte Flag, byte StringCount)
{
    public const ushort PacketIdentifier = 10010;
    public const int FixedPacketLength = 38;
    public const ushort EnterMapAction = 0x4A;
    public const ushort PortalRequestAction = 0x55;
    public const ushort PortalArrivalAction = 0x56;
    public const ushort GetItemSetAction = 0x4B;
    public const ushort GetFriendListAction = 0x4C;
    public const ushort GetWeaponSkillSetAction = 0x4D;
    public const ushort GetMagicSetAction = 0x4E;
    public const ushort GetSyndicateAttributesAction = 0x61;
    public const ushort GetStatisticAction = 0x84;
    public const ushort ReportSilentInfoAction = 0xFB;
    public const ushort ClientStateAppliedAction = 0x198;

    private const int EntityIdOffset = 4;
    private const int ParameterPairOffset = 8;
    private const int ActionParameterOffset = 12;
    private const int TimestampOffset = 16;
    private const int ActionOffset = 20;
    private const int DirectionOffset = 22;
    private const int PositionXOffset = 24;
    private const int PositionYOffset = 26;
    private const int Data1Offset = 28;
    private const int Data2Offset = 32;
    private const int FlagOffset = 36;
    private const int StringCountOffset = 37;

    public static bool TryParse(GameInboundFrame frame, out GameAction action, out GameActionParseError error)
    {
        ArgumentNullException.ThrowIfNull(frame);

        action = default;

        if (frame.PacketId != PacketIdentifier)
        {
            error = GameActionParseError.InvalidPacketId;
            return false;
        }

        if (frame.Header.Length < FixedPacketLength)
        {
            error = GameActionParseError.TruncatedBody;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;

        action = new GameAction(BinaryPrimitives.ReadUInt32LittleEndian(packet[EntityIdOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[ParameterPairOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[ActionParameterOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[TimestampOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[ActionOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[DirectionOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[PositionXOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[PositionYOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[Data1Offset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[Data2Offset..]),
            packet[FlagOffset], packet[StringCountOffset]);

        error = GameActionParseError.None;
        return true;
    }
}
