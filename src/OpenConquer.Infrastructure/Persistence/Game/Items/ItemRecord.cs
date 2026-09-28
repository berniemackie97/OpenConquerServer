using OpenConquer.Domain.Items;

namespace OpenConquer.Infrastructure.Persistence.Game.Items;

internal sealed class ItemRecord
{
    public uint ItemId { get; set; }
    public uint OwnerCharacterId { get; set; }
    public uint ItemTypeId { get; set; }

    public ItemLocationKind LocationKind { get; set; }
    public EquipmentSet? EquipmentSet { get; set; }
    public EquipmentSlot? EquipmentSlot { get; set; }

    public ushort Durability { get; set; }
    public ushort MaximumDurability { get; set; }
    public byte RetailCompatibilityByteA { get; set; }
    public uint TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline { get; set; }
    public byte Socket1Code { get; set; }
    public byte Socket2Code { get; set; }
    public uint HiddenAttackEffect { get; set; }
    public byte RetailCompatibilityByteB { get; set; }
    public byte AdditionLevel { get; set; }
    public byte DamageReductionPercentOrSteedCompositionRed { get; set; }
    public byte ItemBindingCode { get; set; }
    public byte EnchantmentLifeBonusOrSteedCompositionGreen { get; set; }
    public uint MonsterRestraintIdOrSteedCompositionBlue { get; set; }
    public bool IsSuspicious { get; set; }
    public ushort EquipmentLockStateMask { get; set; }
    public DateTime? EquipmentUnlockAtUtc { get; set; }
    public ushort EquipmentColor { get; set; }
    public uint CompositionProgress { get; set; }
    public uint InscribedSyndicateId { get; set; }
    public ushort StackQuantity { get; set; } = 1;

    public ItemLifetimeState LifetimeState { get; set; }
    public int? LifetimeDurationSeconds { get; set; }
    public DateTime? LifetimeExpiresAtUtc { get; set; }
}
