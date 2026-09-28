using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 MsgItem subtype-1 snapshot used for local inventory and tracked equipment.
/// </summary>
public sealed class GameLocalItemSnapshotPacket1008 : IPacket
{
    public const ushort PacketIdentifier = 1008;
    public const int FixedPacketLength = 68;
    public const int PayloadSize = FixedPacketLength - 4;
    public const byte LocalSnapshotKind = 1;
    public const byte InventoryPlacement = 0;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;

    public required uint ItemId { get; init; }
    public required uint ItemTypeId { get; init; }
    public required ushort Durability { get; init; }
    public required ushort MaximumDurability { get; init; }
    public required byte RetailCompatibilityByteA { get; init; }
    public required byte ItemWirePlacement { get; init; }
    public required uint TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline { get; init; }
    public required byte Socket1Code { get; init; }
    public required byte Socket2Code { get; init; }
    public required uint HiddenAttackEffect { get; init; }
    public required byte RetailCompatibilityByteB { get; init; }
    public required byte AdditionLevel { get; init; }
    public required byte DamageReductionPercentOrSteedCompositionRed { get; init; }
    public required byte ItemBindingCode { get; init; }
    public required byte EnchantmentLifeBonusOrSteedCompositionGreen { get; init; }
    public required uint MonsterRestraintIdOrSteedCompositionBlue { get; init; }
    public required bool IsSuspicious { get; init; }
    public required ushort EquipmentLockStateMask { get; init; }
    public required ushort EquipmentColor { get; init; }
    public required uint CompositionProgress { get; init; }
    public required uint InscribedSyndicateId { get; init; }
    public required int WireLifetimeSeconds { get; init; }
    public required ushort StackQuantity { get; init; }

    public void WritePayload(ref PacketWriter writer)
    {
        if (ItemId == 0)
        {
            throw new InvalidOperationException("A local item snapshot requires a nonzero item ID.");
        }

        if (ItemTypeId == 0)
        {
            throw new InvalidOperationException("A local item snapshot requires a nonzero item type ID.");
        }

        if (!IsValidLocalPlacement(ItemWirePlacement))
        {
            throw new InvalidOperationException($"Wire placement {ItemWirePlacement} is not a valid native local inventory or equipment placement.");
        }

        int start = writer.Written;

        writer.WriteUInt32(ItemId);
        writer.WriteUInt32(ItemTypeId);
        writer.WriteUInt16(Durability);
        writer.WriteUInt16(MaximumDurability);
        writer.WriteByte(LocalSnapshotKind);
        writer.WriteByte(RetailCompatibilityByteA);
        writer.WriteByte(ItemWirePlacement);
        writer.Reserve(1);
        writer.WriteUInt32(TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline);
        writer.WriteByte(Socket1Code);
        writer.WriteByte(Socket2Code);
        writer.Reserve(2);
        writer.WriteUInt32(HiddenAttackEffect);
        writer.WriteByte(RetailCompatibilityByteB);
        writer.WriteByte(AdditionLevel);
        writer.WriteByte(DamageReductionPercentOrSteedCompositionRed);
        writer.WriteByte(ItemBindingCode);
        writer.WriteByte(EnchantmentLifeBonusOrSteedCompositionGreen);
        writer.Reserve(3);
        writer.WriteUInt32(MonsterRestraintIdOrSteedCompositionBlue);
        writer.WriteUInt16(IsSuspicious ? (ushort)1 : (ushort)0);
        writer.WriteUInt16(EquipmentLockStateMask);
        writer.WriteUInt16(EquipmentColor);
        writer.Reserve(2);
        writer.WriteUInt32(CompositionProgress);
        writer.WriteUInt32(InscribedSyndicateId);
        writer.WriteUInt32(unchecked((uint)WireLifetimeSeconds));
        writer.WriteUInt16(StackQuantity);
        writer.Reserve(2);

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"MsgItem subtype-1 payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }

    private static bool IsValidLocalPlacement(byte placement)
    {
        return placement is InventoryPlacement or >= 1 and <= 12 or >= 15 and <= 18 or >= 21 and <= 29;
    }
}
