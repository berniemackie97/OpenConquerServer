using System.Buffers.Binary;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Native 5517 MsgEntityAction (10005) eight- or twenty-four-direction movement.
/// The acknowledgement echoes the original request only after authoritative movement commits.
/// </summary>
public readonly record struct GameWalkPacket(uint PackedDirection, uint EntityId, uint CommandSelector, uint MovementTick, uint MapState) : IPacket
{
    public const ushort PacketIdentifier = 10005;
    public const int PacketLength = 24;
    public const uint TwentyFourDirectionSelector = 9;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PacketLength - sizeof(ushort) * 2;
    // The retail handler compares the complete selector to exactly nine, then
    // normalizes only the low direction byte against the selected table length.
    public bool IsTwentyFourDirection => CommandSelector == TwentyFourDirectionSelector;
    public byte DirectionIndex => (byte)(unchecked((byte)PackedDirection) % (IsTwentyFourDirection ? 24 : 8));
    public byte FacingDirection => (byte)(DirectionIndex % 8);

    public static bool TryParse(ReadOnlySpan<byte> packet, out GameWalkPacket walk)
    {
        walk = default;

        if (packet.Length != PacketLength || BinaryPrimitives.ReadUInt16LittleEndian(packet) != PacketLength
            || BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]) != PacketIdentifier)
        {
            return false;
        }

        uint packedDirection = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        uint entityId = BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]);
        uint selector = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);

        if (entityId == 0)
        {
            return false;
        }

        walk = new GameWalkPacket(packedDirection, entityId, selector,
            BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]), BinaryPrimitives.ReadUInt32LittleEndian(packet[20..]));
        return true;
    }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(PackedDirection);
        writer.WriteUInt32(EntityId);
        writer.WriteUInt32(CommandSelector);
        writer.WriteUInt32(MovementTick);
        writer.WriteUInt32(MapState);
    }
}
