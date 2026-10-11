using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes the fixed action packet (10010) body without a trailing string list.
/// </summary>
public sealed class GameActionPacket(uint entityId, uint parameterPair, uint actionParameter, uint timestamp, ushort action, ushort direction, ushort positionX, ushort positionY, uint data1, uint data2, byte flag) : IPacket
{
    public const int PayloadSize = GameAction.FixedPacketLength - 4;

    public ushort PacketId => GameAction.PacketIdentifier;
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

    public static GameActionPacket CreateEnterMapAcknowledgement(uint entityId, uint mapDataId, ushort positionX, ushort positionY, uint timestamp)
    {
        if (entityId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId), "EnterMap acknowledgement requires a nonzero character ID.");
        }

        if (mapDataId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapDataId), "EnterMap acknowledgement requires a nonzero map data ID.");
        }

        return new GameActionPacket(entityId, mapDataId, actionParameter: 0, timestamp, GameAction.EnterMapAction, direction: 0, positionX, positionY, data1: 0, data2: 0, flag: 0);
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
