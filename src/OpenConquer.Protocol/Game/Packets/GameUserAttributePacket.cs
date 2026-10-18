using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>One 5517 user-attribute update (10017), 20-byte record.</summary>
/// <remarks>Attribute 25 uses the whole 16-byte value, not just the low 64 bits.</remarks>
public sealed class GameUserAttributePacket : IPacket
{
    public GameUserAttributePacket(uint entityId, uint attributeId, ulong low, ulong high = 0)
    {
        if (entityId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId));
        }
        if (attributeId is not (LookAttribute or PrimaryStatusAttribute) ||
            (attributeId == LookAttribute && (high != 0 || low > uint.MaxValue)))
        {
            throw new ArgumentOutOfRangeException(nameof(attributeId), "Only proven 5517 look and primary-status updates are implemented.");
        }
        EntityId = entityId;
        AttributeId = attributeId;
        Low = low;
        High = high;
    }

    private uint EntityId { get; }
    private uint AttributeId { get; }
    private ulong Low { get; }
    private ulong High { get; }

    public const ushort PacketIdentifier = 10017;
    public const uint LifeAttribute = 0;
    public const uint LookAttribute = 11;
    public const uint PrimaryStatusAttribute = 25;
    private const int RecordSize = 20;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => 8 + RecordSize;

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(EntityId);
        writer.WriteUInt32(1); // entry count
        writer.WriteUInt32(AttributeId);
        writer.WriteUInt64(Low);
        writer.WriteUInt64(High);
    }
}
