namespace OpenConquer.Domain.Items;

public readonly record struct EquipmentPosition
{
    private EquipmentPosition(EquipmentSet set, EquipmentSlot slot)
    {
        Set = set;
        Slot = slot;
    }

    public EquipmentSet Set { get; }
    public EquipmentSlot Slot { get; }
    public bool IsValid => IsDefinedSet(Set) && IsDefinedSlot(Slot) && (Set != EquipmentSet.Alternate || SupportsAlternateSet(Slot));

    public static EquipmentPosition Create(EquipmentSet set, EquipmentSlot slot)
    {
        if (!IsDefinedSet(set))
        {
            throw new ArgumentOutOfRangeException(nameof(set), set, "Equipment set is not defined.");
        }

        if (!IsDefinedSlot(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Equipment slot is not defined.");
        }

        if (set == EquipmentSet.Alternate && !SupportsAlternateSet(slot))
        {
            throw new ArgumentException($"Equipment slot {slot} does not support the alternate equipment set.", nameof(slot));
        }

        return new EquipmentPosition(set, slot);
    }

    public static bool SupportsAlternateSet(EquipmentSlot slot)
    {
        return slot is EquipmentSlot.Headwear or EquipmentSlot.Necklace or EquipmentSlot.Armor or EquipmentSlot.RightHand or EquipmentSlot.LeftHand
            or EquipmentSlot.Ring or EquipmentSlot.Bottle or EquipmentSlot.Boots or EquipmentSlot.Garment;
    }

    private static bool IsDefinedSet(EquipmentSet set)
    {
        return set is EquipmentSet.Main or EquipmentSet.Alternate;
    }

    private static bool IsDefinedSlot(EquipmentSlot slot)
    {
        return slot is EquipmentSlot.Headwear or EquipmentSlot.Necklace or EquipmentSlot.Armor or EquipmentSlot.RightHand or EquipmentSlot.LeftHand
            or EquipmentSlot.Ring or EquipmentSlot.Bottle or EquipmentSlot.Boots or EquipmentSlot.Garment or EquipmentSlot.Fan or EquipmentSlot.Tower
            or EquipmentSlot.Steed or EquipmentSlot.RightWeaponAccessory or EquipmentSlot.LeftWeaponAccessory or EquipmentSlot.SteedArmor or EquipmentSlot.RidingCrop;
    }
}
