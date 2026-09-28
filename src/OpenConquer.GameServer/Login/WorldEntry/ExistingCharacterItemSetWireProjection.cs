using System.Collections.ObjectModel;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Items;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login.WorldEntry;

internal sealed class ExistingCharacterItemSetWireProjection
{
    private readonly ReadOnlyCollection<GameLocalItemSnapshotPacket1008> _itemSnapshots;

    private ExistingCharacterItemSetWireProjection(CharacterItemSet runtimeItemSet, GameLocalItemSnapshotPacket1008[] itemSnapshots,
        GameActiveEquipmentSnapshotPacket1009? activeEquipmentSnapshot)
    {
        RuntimeItemSet = runtimeItemSet;
        _itemSnapshots = Array.AsReadOnly(itemSnapshots);
        ActiveEquipmentSnapshot = activeEquipmentSnapshot;
    }

    public CharacterItemSet RuntimeItemSet { get; }
    public IReadOnlyList<GameLocalItemSnapshotPacket1008> ItemSnapshots => _itemSnapshots;
    public GameActiveEquipmentSnapshotPacket1009? ActiveEquipmentSnapshot { get; }

    public static ExistingCharacterItemSetWireProjection Create(CharacterItemSet itemSet, ItemTypeDatTable itemTypes, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(itemSet);
        ArgumentNullException.ThrowIfNull(itemTypes);

        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Item-set wire projection requires a UTC timestamp.", nameof(utcNow));
        }

        List<CharacterItem> runtimeItems = new(itemSet.Count);
        List<GameLocalItemSnapshotPacket1008> itemSnapshots = new(itemSet.Count);

        uint headwearItemId = 0;
        uint necklaceItemId = 0;
        uint armorItemId = 0;
        uint rightHandItemId = 0;
        uint leftHandItemId = 0;
        uint ringItemId = 0;
        uint bottleItemId = 0;
        uint bootsItemId = 0;
        uint garmentItemId = 0;
        uint rightWeaponAccessoryItemId = 0;
        uint leftWeaponAccessoryItemId = 0;
        uint steedArmorItemId = 0;
        uint ridingCropItemId = 0;
        bool requiresActiveEquipmentSnapshot = false;

        foreach (CharacterItem item in itemSet.Items)
        {
            if (IsExpired(item.Lifetime, utcNow))
            {
                continue;
            }

            if (!itemTypes.TryGetRecord(item.ItemTypeId, out ItemTypeDatRecord itemType))
            {
                throw new InvalidDataException($"Item {item.ItemId} references unknown item type {item.ItemTypeId}.");
            }

            int wireLifetimeSeconds = CreateWireLifetimeSeconds(item, itemType, utcNow);
            byte wirePlacement = CreateWirePlacement(item);

            runtimeItems.Add(item);
            itemSnapshots.Add(CreateItemSnapshot(item, wirePlacement, wireLifetimeSeconds));

            if (item.Placement.EquipmentPosition is { Set: EquipmentSet.Main } position)
            {
                switch (position.Slot)
                {
                    case EquipmentSlot.Headwear:
                        headwearItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Necklace:
                        necklaceItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Armor:
                        armorItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.RightHand:
                        rightHandItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.LeftHand:
                        leftHandItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Ring:
                        ringItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Bottle:
                        bottleItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Boots:
                        bootsItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.Garment:
                        garmentItemId = item.ItemId;
                        requiresActiveEquipmentSnapshot = true;
                        break;
                    case EquipmentSlot.RightWeaponAccessory:
                        rightWeaponAccessoryItemId = item.ItemId;
                        break;
                    case EquipmentSlot.LeftWeaponAccessory:
                        leftWeaponAccessoryItemId = item.ItemId;
                        break;
                    case EquipmentSlot.SteedArmor:
                        steedArmorItemId = item.ItemId;
                        break;
                    case EquipmentSlot.RidingCrop:
                        ridingCropItemId = item.ItemId;
                        break;
                    case EquipmentSlot.Fan:
                    case EquipmentSlot.Tower:
                    case EquipmentSlot.Steed:
                        break;
                    default:
                        throw new InvalidDataException($"Item {item.ItemId} contains unsupported main equipment slot {(byte)position.Slot}.");
                }
            }
        }

        CharacterItemSet runtimeItemSet = new(itemSet.CharacterId, runtimeItems);

        GameActiveEquipmentSnapshotPacket1009? activeEquipmentSnapshot = requiresActiveEquipmentSnapshot
            ? new GameActiveEquipmentSnapshotPacket1009
            {
                EquipmentMode = GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode,
                HeadwearItemId = headwearItemId,
                NecklaceItemId = necklaceItemId,
                ArmorItemId = armorItemId,
                RightHandItemId = rightHandItemId,
                LeftHandItemId = leftHandItemId,
                RingItemId = ringItemId,
                BottleItemId = bottleItemId,
                BootsItemId = bootsItemId,
                GarmentItemId = garmentItemId,
                RightWeaponAccessoryItemId = rightWeaponAccessoryItemId,
                LeftWeaponAccessoryItemId = leftWeaponAccessoryItemId,
                SteedArmorItemId = steedArmorItemId,
                RidingCropItemId = ridingCropItemId,
            }
            : null;

        return new ExistingCharacterItemSetWireProjection(runtimeItemSet, itemSnapshots.ToArray(), activeEquipmentSnapshot);
    }

    private static bool IsExpired(ItemLifetime lifetime, DateTimeOffset utcNow)
    {
        return lifetime.State == ItemLifetimeState.ActiveExpiry && lifetime.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= utcNow;
    }

    private static int CreateWireLifetimeSeconds(CharacterItem item, ItemTypeDatRecord itemType, DateTimeOffset utcNow)
    {
        switch (item.Lifetime.State)
        {
            case ItemLifetimeState.Permanent:
                if (itemType.StaticLifetimeMinutes > 0)
                {
                    throw new InvalidDataException($"Permanent item {item.ItemId} uses item type {item.ItemTypeId} with positive static lifetime {itemType.StaticLifetimeMinutes} minutes.");
                }

                return 0;

            case ItemLifetimeState.PendingActivation:
                if (itemType.StaticLifetimeMinutes <= 0)
                {
                    throw new InvalidDataException($"Pending-lifetime item {item.ItemId} uses item type {item.ItemTypeId} without a positive static lifetime.");
                }

                int staticLifetimeSeconds;
                try
                {
                    staticLifetimeSeconds = checked(itemType.StaticLifetimeMinutes * 60);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException($"Item type {item.ItemTypeId} static lifetime cannot be represented in seconds.", exception);
                }

                if (item.Lifetime.PendingActivationDurationSeconds != staticLifetimeSeconds)
                {
                    throw new InvalidDataException($"Pending-lifetime item {item.ItemId} duration does not match item type {item.ItemTypeId} static lifetime.");
                }

                return 0;

            case ItemLifetimeState.ActiveExpiry:
                if (item.Lifetime.ExpiresAtUtc is not { } expiresAtUtc)
                {
                    throw new InvalidDataException($"Active-lifetime item {item.ItemId} is missing its expiration timestamp.");
                }

                long remainingTicks = (expiresAtUtc - utcNow).Ticks;
                if (remainingTicks <= 0)
                {
                    throw new InvalidDataException($"Expired item {item.ItemId} reached wire projection instead of being filtered.");
                }

                long remainingSeconds = (remainingTicks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
                if (remainingSeconds > int.MaxValue)
                {
                    throw new InvalidDataException($"Item {item.ItemId} remaining lifetime exceeds the native signed 32-bit wire range.");
                }

                return checked((int)remainingSeconds);

            default:
                throw new InvalidDataException($"Item {item.ItemId} contains unsupported lifetime state {(byte)item.Lifetime.State}.");
        }
    }

    private static byte CreateWirePlacement(CharacterItem item)
    {
        if (item.Placement.IsInventory)
        {
            return GameLocalItemSnapshotPacket1008.InventoryPlacement;
        }

        if (item.Placement.EquipmentPosition is not { } position)
        {
            throw new InvalidDataException($"Item {item.ItemId} contains invalid equipment placement.");
        }

        return position.Set switch
        {
            EquipmentSet.Main => position.Slot switch
            {
                EquipmentSlot.Headwear => 1,
                EquipmentSlot.Necklace => 2,
                EquipmentSlot.Armor => 3,
                EquipmentSlot.RightHand => 4,
                EquipmentSlot.LeftHand => 5,
                EquipmentSlot.Ring => 6,
                EquipmentSlot.Bottle => 7,
                EquipmentSlot.Boots => 8,
                EquipmentSlot.Garment => 9,
                EquipmentSlot.Fan => 10,
                EquipmentSlot.Tower => 11,
                EquipmentSlot.Steed => 12,
                EquipmentSlot.RightWeaponAccessory => 15,
                EquipmentSlot.LeftWeaponAccessory => 16,
                EquipmentSlot.SteedArmor => 17,
                EquipmentSlot.RidingCrop => 18,
                _ => throw new InvalidDataException($"Item {item.ItemId} contains unsupported main equipment slot {(byte)position.Slot}."),
            },
            EquipmentSet.Alternate => position.Slot switch
            {
                EquipmentSlot.Headwear => 21,
                EquipmentSlot.Necklace => 22,
                EquipmentSlot.Armor => 23,
                EquipmentSlot.RightHand => 24,
                EquipmentSlot.LeftHand => 25,
                EquipmentSlot.Ring => 26,
                EquipmentSlot.Bottle => 27,
                EquipmentSlot.Boots => 28,
                EquipmentSlot.Garment => 29,
                _ => throw new InvalidDataException($"Item {item.ItemId} contains unsupported alternate equipment slot {(byte)position.Slot}."),
            },
            _ => throw new InvalidDataException($"Item {item.ItemId} contains unsupported equipment set {(byte)position.Set}."),
        };
    }

    private static GameLocalItemSnapshotPacket1008 CreateItemSnapshot(CharacterItem item, byte wirePlacement, int wireLifetimeSeconds)
    {
        return new GameLocalItemSnapshotPacket1008
        {
            ItemId = item.ItemId,
            ItemTypeId = item.ItemTypeId,
            Durability = item.Durability,
            MaximumDurability = item.MaximumDurability,
            RetailCompatibilityByteA = item.RetailCompatibilityByteA,
            ItemWirePlacement = wirePlacement,
            TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline = item.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline,
            Socket1Code = item.Socket1Code,
            Socket2Code = item.Socket2Code,
            HiddenAttackEffect = item.HiddenAttackEffect,
            RetailCompatibilityByteB = item.RetailCompatibilityByteB,
            AdditionLevel = item.AdditionLevel,
            DamageReductionPercentOrSteedCompositionRed = item.DamageReductionPercentOrSteedCompositionRed,
            ItemBindingCode = item.ItemBindingCode,
            EnchantmentLifeBonusOrSteedCompositionGreen = item.EnchantmentLifeBonusOrSteedCompositionGreen,
            MonsterRestraintIdOrSteedCompositionBlue = item.MonsterRestraintIdOrSteedCompositionBlue,
            IsSuspicious = item.IsSuspicious,
            EquipmentLockStateMask = item.EquipmentLockStateMask,
            EquipmentColor = item.EquipmentColor,
            CompositionProgress = item.CompositionProgress,
            InscribedSyndicateId = item.InscribedSyndicateId,
            WireLifetimeSeconds = wireLifetimeSeconds,
            StackQuantity = item.StackQuantity,
        };
    }
}
