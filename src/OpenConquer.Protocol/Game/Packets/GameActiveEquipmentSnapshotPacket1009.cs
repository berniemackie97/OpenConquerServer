using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 MsgTick subtype-46 active-equipment snapshot.
/// </summary>
public sealed class GameActiveEquipmentSnapshotPacket1009 : IPacket
{
    public const ushort PacketIdentifier = 1009;
    public const int FixedPacketLength = 88;
    public const int PayloadSize = FixedPacketLength - 4;
    public const ushort ActiveEquipmentSnapshotSubtype = 46;
    public const uint MainEquipmentMode = 0;
    public const uint AlternateEquipmentMode = 1;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;

    public required uint EquipmentMode { get; init; }
    public required uint HeadwearItemId { get; init; }
    public required uint NecklaceItemId { get; init; }
    public required uint ArmorItemId { get; init; }
    public required uint RightHandItemId { get; init; }
    public required uint LeftHandItemId { get; init; }
    public required uint RingItemId { get; init; }
    public required uint BottleItemId { get; init; }
    public required uint BootsItemId { get; init; }
    public required uint GarmentItemId { get; init; }
    public required uint RightWeaponAccessoryItemId { get; init; }
    public required uint LeftWeaponAccessoryItemId { get; init; }
    public required uint SteedArmorItemId { get; init; }
    public required uint RidingCropItemId { get; init; }

    public void WritePayload(ref PacketWriter writer)
    {
        if (EquipmentMode > AlternateEquipmentMode)
        {
            throw new InvalidOperationException($"Equipment mode {EquipmentMode} is not a valid native equipment mode.");
        }

        int start = writer.Written;

        writer.Reserve(sizeof(uint));
        writer.WriteUInt32(EquipmentMode);
        writer.WriteUInt16(ActiveEquipmentSnapshotSubtype);
        writer.Reserve(sizeof(ushort));
        writer.Reserve(sizeof(uint));
        writer.Reserve(sizeof(uint));
        writer.Reserve(sizeof(uint));
        writer.Reserve(sizeof(uint));

        writer.WriteUInt32(HeadwearItemId);
        writer.WriteUInt32(NecklaceItemId);
        writer.WriteUInt32(ArmorItemId);
        writer.WriteUInt32(RightHandItemId);
        writer.WriteUInt32(LeftHandItemId);
        writer.WriteUInt32(RingItemId);
        writer.WriteUInt32(BottleItemId);
        writer.WriteUInt32(BootsItemId);
        writer.WriteUInt32(GarmentItemId);
        writer.WriteUInt32(RightWeaponAccessoryItemId);
        writer.WriteUInt32(LeftWeaponAccessoryItemId);
        writer.WriteUInt32(SteedArmorItemId);
        writer.WriteUInt32(RidingCropItemId);

        writer.Reserve(sizeof(uint));

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"MsgTick subtype-46 payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
