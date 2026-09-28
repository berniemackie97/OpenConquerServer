using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Hydration;

public sealed class CharacterItem
{
    private const ushort PendingUnlockStateMask = 2;

    public CharacterItem(uint itemId, uint ownerCharacterId, uint itemTypeId, ItemPlacement placement,
        ushort durability, ushort maximumDurability, byte retailCompatibilityByteA,
        uint talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline,
        byte socket1Code, byte socket2Code, uint hiddenAttackEffect, byte retailCompatibilityByteB,
        byte additionLevel, byte damageReductionPercentOrSteedCompositionRed, byte itemBindingCode,
        byte enchantmentLifeBonusOrSteedCompositionGreen, uint monsterRestraintIdOrSteedCompositionBlue,
        bool isSuspicious, ushort equipmentLockStateMask, DateTimeOffset? equipmentUnlockAtUtc,
        ushort equipmentColor, uint compositionProgress, uint inscribedSyndicateId,
        ushort stackQuantity, ItemLifetime lifetime)
    {
        if (itemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), "A hydrated item requires a nonzero item ID.");
        }

        if (!CharacterIdentityPolicy.IsPlayerEntityId(ownerCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerCharacterId), $"A hydrated item owner ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        if (itemTypeId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemTypeId), "A hydrated item requires a nonzero item type ID.");
        }

        if (!placement.IsValid)
        {
            throw new ArgumentException("A hydrated item requires a valid placement.", nameof(placement));
        }

        if (stackQuantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stackQuantity), "A hydrated item requires a positive stack quantity.");
        }

        bool pendingUnlock = (equipmentLockStateMask & PendingUnlockStateMask) != 0;
        if (pendingUnlock != equipmentUnlockAtUtc.HasValue)
        {
            throw new ArgumentException("The equipment unlock schedule must match the pending-unlock state.", nameof(equipmentUnlockAtUtc));
        }

        if (equipmentUnlockAtUtc is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new ArgumentException("An equipment unlock timestamp must use the UTC offset.", nameof(equipmentUnlockAtUtc));
        }

        if (!lifetime.IsValid)
        {
            throw new ArgumentException("A hydrated item requires a valid lifetime.", nameof(lifetime));
        }

        ItemId = itemId;
        OwnerCharacterId = ownerCharacterId;
        ItemTypeId = itemTypeId;
        Placement = placement;
        Durability = durability;
        MaximumDurability = maximumDurability;
        RetailCompatibilityByteA = retailCompatibilityByteA;
        TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline = talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline;
        Socket1Code = socket1Code;
        Socket2Code = socket2Code;
        HiddenAttackEffect = hiddenAttackEffect;
        RetailCompatibilityByteB = retailCompatibilityByteB;
        AdditionLevel = additionLevel;
        DamageReductionPercentOrSteedCompositionRed = damageReductionPercentOrSteedCompositionRed;
        ItemBindingCode = itemBindingCode;
        EnchantmentLifeBonusOrSteedCompositionGreen = enchantmentLifeBonusOrSteedCompositionGreen;
        MonsterRestraintIdOrSteedCompositionBlue = monsterRestraintIdOrSteedCompositionBlue;
        IsSuspicious = isSuspicious;
        EquipmentLockStateMask = equipmentLockStateMask;
        EquipmentUnlockAtUtc = equipmentUnlockAtUtc;
        EquipmentColor = equipmentColor;
        CompositionProgress = compositionProgress;
        InscribedSyndicateId = inscribedSyndicateId;
        StackQuantity = stackQuantity;
        Lifetime = lifetime;
    }

    public uint ItemId { get; }
    public uint OwnerCharacterId { get; }
    public uint ItemTypeId { get; }
    public ItemPlacement Placement { get; }

    public ushort Durability { get; }
    public ushort MaximumDurability { get; }
    public byte RetailCompatibilityByteA { get; }
    public uint TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline { get; }
    public byte Socket1Code { get; }
    public byte Socket2Code { get; }
    public uint HiddenAttackEffect { get; }
    public byte RetailCompatibilityByteB { get; }
    public byte AdditionLevel { get; }
    public byte DamageReductionPercentOrSteedCompositionRed { get; }
    public byte ItemBindingCode { get; }
    public byte EnchantmentLifeBonusOrSteedCompositionGreen { get; }
    public uint MonsterRestraintIdOrSteedCompositionBlue { get; }
    public bool IsSuspicious { get; }

    public ushort EquipmentLockStateMask { get; }
    public DateTimeOffset? EquipmentUnlockAtUtc { get; }

    public ushort EquipmentColor { get; }
    public uint CompositionProgress { get; }
    public uint InscribedSyndicateId { get; }
    public ushort StackQuantity { get; }
    public ItemLifetime Lifetime { get; }
}
