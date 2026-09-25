using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

public enum GameActionParseError
{
    None = 0,
    InvalidPacketId,
    TruncatedBody,
}

/// <summary>
/// Represents the verified fixed body of native 5517 MsgAction packet 10010.
/// </summary>
public readonly record struct GameAction10010(uint EntityId, uint ParameterPair, uint ActionParameter, uint Timestamp, ushort Action, ushort Direction, ushort PositionX, ushort PositionY, uint Data1, uint Data2, byte Flag, byte StringCount)
{
    public const ushort PacketIdentifier = 10010;
    public const int FixedPacketLength = 38;
    public const ushort EnterMapAction = 0x4A;
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

    public static bool TryParse(GameInboundFrame frame, out GameAction10010 action, out GameActionParseError error)
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

        action = new GameAction10010(BinaryPrimitives.ReadUInt32LittleEndian(packet[EntityIdOffset..]),
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

/// <summary>
/// Writes the fixed native 5517 MsgAction packet body without a trailing string list.
/// </summary>
public sealed class GameActionPacket10010(uint entityId, uint parameterPair, uint actionParameter, uint timestamp, ushort action, ushort direction, ushort positionX, ushort positionY, uint data1, uint data2, byte flag) : IPacket
{
    public const int PayloadSize = GameAction10010.FixedPacketLength - 4;

    public ushort PacketId => GameAction10010.PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint EntityId { get; } = entityId;
    public uint ParameterPair { get; } = parameterPair;
    public uint ActionParameter { get; } = actionParameter;
    public uint Timestamp { get; } = timestamp;
    public ushort Action { get; } = action;
    public ushort Direction { get; } = direction;
    public ushort PositionX { get; } = positionX;
    public ushort PositionY { get; } = positionY;
    public uint Data1 { get; } = data1;
    public uint Data2 { get; } = data2;
    public byte Flag { get; } = flag;

    public static GameActionPacket10010 CreateEnterMapAcknowledgement(uint entityId, uint mapDataId, ushort positionX, ushort positionY, uint timestamp)
    {
        if (entityId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId), "EnterMap acknowledgement requires a nonzero character ID.");
        }

        if (mapDataId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapDataId), "EnterMap acknowledgement requires a nonzero map data ID.");
        }

        return new GameActionPacket10010(entityId, mapDataId, actionParameter: 0, timestamp, GameAction10010.EnterMapAction, direction: 0, positionX, positionY, data1: 0, data2: 0, flag: 0);
    }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(EntityId);
        writer.WriteUInt32(ParameterPair);
        writer.WriteUInt32(ActionParameter);
        writer.WriteUInt32(Timestamp);
        writer.WriteUInt16(Action);
        writer.WriteUInt16(Direction);
        writer.WriteUInt16(PositionX);
        writer.WriteUInt16(PositionY);
        writer.WriteUInt32(Data1);
        writer.WriteUInt32(Data2);
        writer.WriteByte(Flag);
        writer.WriteByte(0);
    }
}
