using System.Globalization;
using System.Text;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterItemSetWireProjectionTests
{
    private const int SeedTableLength = 128;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint DefaultItemTypeId = 100_000;

    private static readonly DateTimeOffset s_utcNow = new(2026, 9, 28, 21, 0, 0, TimeSpan.Zero);
    private static readonly Encoding s_retailEncoding = CreateRetailEncoding();

    [Fact]
    public void Create_EmptyItemSet_ReturnsEmptyProjection()
    {
        CharacterItemSet itemSet = new(CharacterId, []);
        ItemTypeDatTable itemTypes = CreateItemTypeTable();

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal(CharacterId, projection.RuntimeItemSet.CharacterId);
        Assert.Empty(projection.RuntimeItemSet.Items);
        Assert.Empty(projection.ItemSnapshots);
        Assert.Null(projection.ActiveEquipmentSnapshot);
    }

    [Fact]
    public void Create_InventoryItem_UsesNativeInventoryPlacement()
    {
        CharacterItem item = CreateItem(itemId: 1);
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        GameLocalItemSnapshotPacket1008 snapshot = Assert.Single(projection.ItemSnapshots);
        Assert.Equal(GameLocalItemSnapshotPacket1008.InventoryPlacement, snapshot.ItemWirePlacement);
    }

    [Theory]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Headwear, 1)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Necklace, 2)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Armor, 3)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.RightHand, 4)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.LeftHand, 5)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Ring, 6)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Bottle, 7)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Boots, 8)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Garment, 9)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Fan, 10)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Tower, 11)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.Steed, 12)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.RightWeaponAccessory, 15)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.LeftWeaponAccessory, 16)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.SteedArmor, 17)]
    [InlineData(EquipmentSet.Main, EquipmentSlot.RidingCrop, 18)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Headwear, 21)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Necklace, 22)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Armor, 23)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.RightHand, 24)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.LeftHand, 25)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Ring, 26)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Bottle, 27)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Boots, 28)]
    [InlineData(EquipmentSet.Alternate, EquipmentSlot.Garment, 29)]
    public void Create_EquipmentItem_MapsVerifiedNativePlacement(EquipmentSet set, EquipmentSlot slot, byte expectedPlacement)
    {
        CharacterItem item = CreateItem(itemId: 1, placement: CreateEquipmentPlacement(set, slot));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        GameLocalItemSnapshotPacket1008 snapshot = Assert.Single(projection.ItemSnapshots);
        Assert.Equal(expectedPlacement, snapshot.ItemWirePlacement);
    }

    [Fact]
    public void Create_ItemSnapshot_PreservesCompleteHydratedWireState()
    {
        DateTimeOffset unlockAtUtc = s_utcNow.AddHours(4);
        CharacterItem item = new(0x01020304, CharacterId, DefaultItemTypeId, ItemPlacement.CreateInventory(),
            durability: 0x0506, maximumDurability: 0x0708, retailCompatibilityByteA: 0x09,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 0x0A0B0C0D,
            socket1Code: 0x0E, socket2Code: 0x0F, hiddenAttackEffect: 0x10111213, retailCompatibilityByteB: 0x14,
            additionLevel: 0x15, damageReductionPercentOrSteedCompositionRed: 0x16, itemBindingCode: 0x17,
            enchantmentLifeBonusOrSteedCompositionGreen: 0x18, monsterRestraintIdOrSteedCompositionBlue: 0x191A1B1C,
            isSuspicious: true, equipmentLockStateMask: 0x8002, equipmentUnlockAtUtc: unlockAtUtc,
            equipmentColor: 0x1D1E, compositionProgress: 0x1F202122, inscribedSyndicateId: 0x23242526,
            stackQuantity: 0x2728, lifetime: ItemLifetime.CreatePermanent());
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        GameLocalItemSnapshotPacket1008 snapshot = Assert.Single(projection.ItemSnapshots);
        Assert.Equal(item.ItemId, snapshot.ItemId);
        Assert.Equal(item.ItemTypeId, snapshot.ItemTypeId);
        Assert.Equal(item.Durability, snapshot.Durability);
        Assert.Equal(item.MaximumDurability, snapshot.MaximumDurability);
        Assert.Equal(item.RetailCompatibilityByteA, snapshot.RetailCompatibilityByteA);
        Assert.Equal(GameLocalItemSnapshotPacket1008.InventoryPlacement, snapshot.ItemWirePlacement);
        Assert.Equal(item.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline, snapshot.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline);
        Assert.Equal(item.Socket1Code, snapshot.Socket1Code);
        Assert.Equal(item.Socket2Code, snapshot.Socket2Code);
        Assert.Equal(item.HiddenAttackEffect, snapshot.HiddenAttackEffect);
        Assert.Equal(item.RetailCompatibilityByteB, snapshot.RetailCompatibilityByteB);
        Assert.Equal(item.AdditionLevel, snapshot.AdditionLevel);
        Assert.Equal(item.DamageReductionPercentOrSteedCompositionRed, snapshot.DamageReductionPercentOrSteedCompositionRed);
        Assert.Equal(item.ItemBindingCode, snapshot.ItemBindingCode);
        Assert.Equal(item.EnchantmentLifeBonusOrSteedCompositionGreen, snapshot.EnchantmentLifeBonusOrSteedCompositionGreen);
        Assert.Equal(item.MonsterRestraintIdOrSteedCompositionBlue, snapshot.MonsterRestraintIdOrSteedCompositionBlue);
        Assert.Equal(item.IsSuspicious, snapshot.IsSuspicious);
        Assert.Equal(item.EquipmentLockStateMask, snapshot.EquipmentLockStateMask);
        Assert.Equal(item.EquipmentColor, snapshot.EquipmentColor);
        Assert.Equal(item.CompositionProgress, snapshot.CompositionProgress);
        Assert.Equal(item.InscribedSyndicateId, snapshot.InscribedSyndicateId);
        Assert.Equal(0, snapshot.WireLifetimeSeconds);
        Assert.Equal(item.StackQuantity, snapshot.StackQuantity);
    }

    [Fact]
    public void Create_MainEquipment_BuildsCompleteActiveEquipmentSnapshot()
    {
        CharacterItemSet itemSet = new(CharacterId,
        [
            CreateItem(1, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Headwear)),
            CreateItem(2, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Necklace)),
            CreateItem(3, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Armor)),
            CreateItem(4, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.RightHand)),
            CreateItem(5, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.LeftHand)),
            CreateItem(6, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Ring)),
            CreateItem(7, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Bottle)),
            CreateItem(8, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Boots)),
            CreateItem(9, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Garment)),
            CreateItem(10, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.RightWeaponAccessory)),
            CreateItem(11, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.LeftWeaponAccessory)),
            CreateItem(12, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.SteedArmor)),
            CreateItem(13, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.RidingCrop)),
        ]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        GameActiveEquipmentSnapshotPacket1009 snapshot = Assert.IsType<GameActiveEquipmentSnapshotPacket1009>(projection.ActiveEquipmentSnapshot);
        Assert.Equal(GameActiveEquipmentSnapshotPacket1009.MainEquipmentMode, snapshot.EquipmentMode);
        Assert.Equal(1u, snapshot.HeadwearItemId);
        Assert.Equal(2u, snapshot.NecklaceItemId);
        Assert.Equal(3u, snapshot.ArmorItemId);
        Assert.Equal(4u, snapshot.RightHandItemId);
        Assert.Equal(5u, snapshot.LeftHandItemId);
        Assert.Equal(6u, snapshot.RingItemId);
        Assert.Equal(7u, snapshot.BottleItemId);
        Assert.Equal(8u, snapshot.BootsItemId);
        Assert.Equal(9u, snapshot.GarmentItemId);
        Assert.Equal(10u, snapshot.RightWeaponAccessoryItemId);
        Assert.Equal(11u, snapshot.LeftWeaponAccessoryItemId);
        Assert.Equal(12u, snapshot.SteedArmorItemId);
        Assert.Equal(13u, snapshot.RidingCropItemId);
    }

    [Fact]
    public void Create_NonSnapshotEquipmentOnly_DoesNotEmitActiveEquipmentSnapshot()
    {
        CharacterItemSet itemSet = new(CharacterId,
        [
            CreateItem(1, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Fan)),
            CreateItem(2, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Tower)),
            CreateItem(3, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Steed)),
            CreateItem(4, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.RightWeaponAccessory)),
            CreateItem(5, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.LeftWeaponAccessory)),
            CreateItem(6, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.SteedArmor)),
            CreateItem(7, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.RidingCrop)),
            CreateItem(8, placement: CreateEquipmentPlacement(EquipmentSet.Alternate, EquipmentSlot.Headwear)),
        ]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal(8, projection.ItemSnapshots.Count);
        Assert.Null(projection.ActiveEquipmentSnapshot);
    }

    [Fact]
    public void Create_ExpiredMainEquipment_IsRemovedFromRuntimePacketsAndActiveSnapshot()
    {
        CharacterItem expiredHeadwear = CreateItem(1, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Headwear),
            lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow));
        CharacterItem activeArmor = CreateItem(2, placement: CreateEquipmentPlacement(EquipmentSet.Main, EquipmentSlot.Armor));
        CharacterItemSet itemSet = new(CharacterId, [expiredHeadwear, activeArmor]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        CharacterItem runtimeItem = Assert.Single(projection.RuntimeItemSet.Items);
        GameLocalItemSnapshotPacket1008 itemSnapshot = Assert.Single(projection.ItemSnapshots);
        GameActiveEquipmentSnapshotPacket1009 activeEquipment = Assert.IsType<GameActiveEquipmentSnapshotPacket1009>(projection.ActiveEquipmentSnapshot);

        Assert.Equal(2u, runtimeItem.ItemId);
        Assert.Equal(2u, itemSnapshot.ItemId);
        Assert.Equal(0u, activeEquipment.HeadwearItemId);
        Assert.Equal(2u, activeEquipment.ArmorItemId);
    }

    [Fact]
    public void Create_ItemsRemainDeterministicallyOrderedAfterFiltering()
    {
        CharacterItemSet itemSet = new(CharacterId,
        [
            CreateItem(30),
            CreateItem(10),
            CreateItem(20, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow)),
        ]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal([10u, 30u], projection.RuntimeItemSet.Items.Select(static item => item.ItemId));
        Assert.Equal([10u, 30u], projection.ItemSnapshots.Select(static packet => packet.ItemId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_PermanentItemWithoutPositiveStaticLifetime_WritesZeroLifetime(int staticLifetimeMinutes)
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePermanent());
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, staticLifetimeMinutes));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal(0, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Fact]
    public void Create_PermanentItemWithPositiveStaticLifetime_FailsClosed()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePermanent());
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 5));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains(item.ItemId.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains("positive static lifetime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PendingActivationMatchingStaticLifetime_WritesZeroWithoutActivating()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePendingActivation(300));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 5));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        GameLocalItemSnapshotPacket1008 snapshot = Assert.Single(projection.ItemSnapshots);
        CharacterItem runtimeItem = Assert.Single(projection.RuntimeItemSet.Items);

        Assert.Equal(0, snapshot.WireLifetimeSeconds);
        Assert.Equal(ItemLifetimeState.PendingActivation, runtimeItem.Lifetime.State);
        Assert.Equal(300, runtimeItem.Lifetime.PendingActivationDurationSeconds);
        Assert.Null(runtimeItem.Lifetime.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_PendingActivationWithoutPositiveStaticLifetime_FailsClosed(int staticLifetimeMinutes)
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePendingActivation(300));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, staticLifetimeMinutes));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains("without a positive static lifetime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PendingActivationDurationMismatch_FailsClosed()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePendingActivation(299));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 5));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PendingActivationStaticLifetimeOverflow_FailsClosed()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePendingActivation(int.MaxValue));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, int.MaxValue));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains("cannot be represented in seconds", exception.Message, StringComparison.Ordinal);
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void Create_ActiveExpiry_WritesExactRemainingWholeSeconds()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddSeconds(90)));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 5));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal(90, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Fact]
    public void Create_ActiveExpiryWithFractionalSecond_CeilingRoundsToPositiveSecond()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(1)));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 5));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Equal(1, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ExpiredActiveItem_IsFilteredBeforeStaticAssetLookup(long expirationOffsetTicks)
    {
        const uint unknownItemTypeId = 999_999;
        CharacterItem item = CreateItem(1, itemTypeId: unknownItemTypeId,
            lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(expirationOffsetTicks)));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow);

        Assert.Empty(projection.RuntimeItemSet.Items);
        Assert.Empty(projection.ItemSnapshots);
        Assert.Null(projection.ActiveEquipmentSnapshot);
    }

    [Fact]
    public void Create_ActiveExpiryOutsideNativeSignedRange_FailsClosed()
    {
        long remainingTicks = ((long)int.MaxValue + 1L) * TimeSpan.TicksPerSecond;
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(remainingTicks)));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains("signed 32-bit wire range", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UnknownActiveItemType_FailsClosed()
    {
        const uint unknownItemTypeId = 999_999;
        CharacterItem item = CreateItem(1, itemTypeId: unknownItemTypeId,
            lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddMinutes(5)));
        CharacterItemSet itemSet = new(CharacterId, [item]);
        ItemTypeDatTable itemTypes = CreateItemTypeTable((DefaultItemTypeId, 0));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, s_utcNow));

        Assert.Contains(unknownItemTypeId.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains("unknown item type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NonUtcProjectionTimestamp_IsRejected()
    {
        CharacterItemSet itemSet = new(CharacterId, []);
        ItemTypeDatTable itemTypes = CreateItemTypeTable();
        DateTimeOffset nonUtc = s_utcNow.ToOffset(TimeSpan.FromHours(-4));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ExistingCharacterItemSetWireProjection.Create(itemSet, itemTypes, nonUtc));

        Assert.Equal("utcNow", exception.ParamName);
    }

    private static CharacterItem CreateItem(uint itemId, uint itemTypeId = DefaultItemTypeId, ItemPlacement? placement = null,
        ItemLifetime? lifetime = null, ushort stackQuantity = 1)
    {
        return new CharacterItem(itemId, CharacterId, itemTypeId, placement ?? ItemPlacement.CreateInventory(),
            durability: 100, maximumDurability: 100, retailCompatibilityByteA: 0,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 0,
            socket1Code: 0, socket2Code: 0, hiddenAttackEffect: 0, retailCompatibilityByteB: 0,
            additionLevel: 0, damageReductionPercentOrSteedCompositionRed: 0, itemBindingCode: 0,
            enchantmentLifeBonusOrSteedCompositionGreen: 0, monsterRestraintIdOrSteedCompositionBlue: 0,
            isSuspicious: false, equipmentLockStateMask: 0, equipmentUnlockAtUtc: null,
            equipmentColor: 0, compositionProgress: 0, inscribedSyndicateId: 0,
            stackQuantity, lifetime ?? ItemLifetime.CreatePermanent());
    }

    private static ItemPlacement CreateEquipmentPlacement(EquipmentSet set, EquipmentSlot slot)
    {
        return ItemPlacement.CreateEquipment(EquipmentPosition.Create(set, slot));
    }

    private static ItemTypeDatTable CreateItemTypeTable(params (uint ItemTypeId, int StaticLifetimeMinutes)[] records)
    {
        if (records.Length == 0)
        {
            records = [(DefaultItemTypeId, 0)];
        }

        string decodedText = string.Join("\r\n", records.Select(static record =>
            CreateLine(CreateFields(record.ItemTypeId, record.StaticLifetimeMinutes))));

        return ItemTypeDatTable.Parse(EncodeText(decodedText));
    }

    private static string[] CreateFields(uint itemTypeId, int staticLifetimeMinutes)
    {
        string[] fields = Enumerable.Repeat("0", ItemTypeDatRecord.NativeParsedFieldCount).ToArray();
        fields[ItemTypeDatRecord.ItemTypeIdFieldIndex] = itemTypeId.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.NameFieldIndex] = $"Item{itemTypeId}";
        fields[ItemTypeDatRecord.RequiredLevelFieldIndex] = "0";
        fields[ItemTypeDatRecord.SpeedPercentOffsetFieldIndex] = "0";
        fields[ItemTypeDatRecord.LifeFieldIndex] = "0";
        fields[ItemTypeDatRecord.ManaFieldIndex] = "0";
        fields[ItemTypeDatRecord.StaticLifetimeMinutesFieldIndex] = staticLifetimeMinutes.ToString(CultureInfo.InvariantCulture);
        fields[ItemTypeDatRecord.StackCapacityFieldIndex] = "1";
        fields[ItemTypeDatRecord.TypeDescriptionFieldIndex] = string.Empty;
        fields[ItemTypeDatRecord.ItemDescriptionFieldIndex] = string.Empty;
        return fields;
    }

    private static string CreateLine(string[] fields)
    {
        return string.Join("@@", fields) + "@@";
    }

    private static byte[] EncodeText(string decodedText)
    {
        byte[] encodedPayload = s_retailEncoding.GetBytes(decodedText);
        Span<byte> seedTable = stackalloc byte[SeedTableLength];
        BuildSeedTable(seedTable, ItemTypeDatTable.DecodedTextSeed);

        for (int index = 0; index < encodedPayload.Length; index++)
        {
            int rotation = index & 7;
            byte transformed = rotation == 0 ? encodedPayload[index] : RotateLeft(encodedPayload[index], rotation);
            encodedPayload[index] = (byte)(transformed ^ seedTable[index % SeedTableLength]);
        }

        return encodedPayload;
    }

    private static void BuildSeedTable(Span<byte> seedTable, int seed)
    {
        uint state = unchecked((uint)seed);

        for (int index = 0; index < seedTable.Length; index++)
        {
            state = unchecked(state * 214013u + 2531011u);
            seedTable[index] = (byte)(((state >> 16) & 0x7FFFu) % 256u);
        }
    }

    private static byte RotateLeft(byte value, int bitCount)
    {
        return (byte)((value << bitCount) | (value >> (8 - bitCount)));
    }

    private static Encoding CreateRetailEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(ItemTypeDatTable.RetailCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
