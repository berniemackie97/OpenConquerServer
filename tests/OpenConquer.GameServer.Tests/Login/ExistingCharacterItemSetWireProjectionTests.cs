using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterItemSetWireProjectionTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint ItemTypeId = 100_000;
    private static readonly DateTimeOffset s_utcNow = new(2026, 9, 28, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_EmptyItemSet_ReturnsEmptyProjection()
    {
        CharacterItemSet itemSet = new(CharacterId, []);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Same(itemSet, projection.RuntimeItemSet);
        Assert.Empty(projection.ItemSnapshots);
        Assert.Null(projection.ActiveEquipmentSnapshot);
    }

    [Fact]
    public void Create_InventoryItem_UsesNativeInventoryPlacement()
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1)]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(GameLocalItemSnapshotPacket1008.InventoryPlacement, Assert.Single(projection.ItemSnapshots).ItemWirePlacement);
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
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1, placement: ItemPlacement.CreateEquipment(EquipmentPosition.Create(set, slot)))]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(expectedPlacement, Assert.Single(projection.ItemSnapshots).ItemWirePlacement);
    }

    [Fact]
    public void Create_ItemSnapshot_PreservesCompleteRuntimeWireState()
    {
        CharacterItem item = new(0x01020304, CharacterId, ItemTypeId, ItemPlacement.CreateInventory(),
            0x0506, 0x0708, 0x09, 0x0A0B0C0D, 0x0E, 0x0F, 0x10111213, 0x14, 0x15, 0x16, 0x17, 0x18, 0x191A1B1C,
            true, 0x8002, s_utcNow.AddHours(4), 0x1D1E, 0x1F202122, 0x23242526, 0x2728, ItemLifetime.CreatePermanent());
        CharacterItemSet itemSet = new(CharacterId, [item]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);
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
            CreateEquipmentItem(1, EquipmentSlot.Headwear), CreateEquipmentItem(2, EquipmentSlot.Necklace),
            CreateEquipmentItem(3, EquipmentSlot.Armor), CreateEquipmentItem(4, EquipmentSlot.RightHand),
            CreateEquipmentItem(5, EquipmentSlot.LeftHand), CreateEquipmentItem(6, EquipmentSlot.Ring),
            CreateEquipmentItem(7, EquipmentSlot.Bottle), CreateEquipmentItem(8, EquipmentSlot.Boots),
            CreateEquipmentItem(9, EquipmentSlot.Garment), CreateEquipmentItem(10, EquipmentSlot.RightWeaponAccessory),
            CreateEquipmentItem(11, EquipmentSlot.LeftWeaponAccessory), CreateEquipmentItem(12, EquipmentSlot.SteedArmor),
            CreateEquipmentItem(13, EquipmentSlot.RidingCrop),
        ]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);
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
            CreateEquipmentItem(1, EquipmentSlot.Fan), CreateEquipmentItem(2, EquipmentSlot.Tower),
            CreateEquipmentItem(3, EquipmentSlot.Steed), CreateEquipmentItem(4, EquipmentSlot.RightWeaponAccessory),
            CreateEquipmentItem(5, EquipmentSlot.LeftWeaponAccessory), CreateEquipmentItem(6, EquipmentSlot.SteedArmor),
            CreateEquipmentItem(7, EquipmentSlot.RidingCrop),
            CreateItem(8, placement: ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Alternate, EquipmentSlot.Headwear))),
        ]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(8, projection.ItemSnapshots.Count);
        Assert.Null(projection.ActiveEquipmentSnapshot);
    }

    [Fact]
    public void Create_ItemSnapshots_PreserveDeterministicItemOrder()
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(30), CreateItem(10), CreateItem(20)]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Same(itemSet, projection.RuntimeItemSet);
        Assert.Equal([10u, 20u, 30u], projection.ItemSnapshots.Select(static packet => packet.ItemId));
    }

    [Fact]
    public void Create_PermanentItem_WritesZeroLifetime()
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1)]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(0, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Fact]
    public void Create_PendingActivationItem_WritesZeroWithoutActivating()
    {
        CharacterItem item = CreateItem(1, lifetime: ItemLifetime.CreatePendingActivation(300));
        CharacterItemSet itemSet = new(CharacterId, [item]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(0, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
        Assert.Equal(ItemLifetimeState.PendingActivation, Assert.Single(projection.RuntimeItemSet.Items).Lifetime.State);
    }

    [Fact]
    public void Create_ActiveExpiry_WritesExactRemainingWholeSeconds()
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddSeconds(90)))]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(90, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Fact]
    public void Create_ActiveExpiryWithFractionalSecond_CeilingRoundsToPositiveSecond()
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(1)))]);

        ExistingCharacterItemSetWireProjection projection = ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow);

        Assert.Equal(1, Assert.Single(projection.ItemSnapshots).WireLifetimeSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ExpiredActiveItem_FailsClosedBecauseResolutionShouldHaveRemovedIt(long offsetTicks)
    {
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(offsetTicks)))]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow));

        Assert.Contains("instead of being filtered during item-set resolution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ActiveExpiryOutsideNativeSignedRange_FailsClosed()
    {
        long remainingTicks = ((long)int.MaxValue + 1L) * TimeSpan.TicksPerSecond;
        CharacterItemSet itemSet = new(CharacterId, [CreateItem(1, lifetime: ItemLifetime.CreateActiveExpiry(s_utcNow.AddTicks(remainingTicks)))]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow));

        Assert.Contains("signed 32-bit wire range", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NonUtcTimestamp_IsRejected()
    {
        CharacterItemSet itemSet = new(CharacterId, []);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ExistingCharacterItemSetWireProjection.Create(itemSet, s_utcNow.ToOffset(TimeSpan.FromHours(-4))));

        Assert.Equal("utcNow", exception.ParamName);
    }

    private static CharacterItem CreateEquipmentItem(uint itemId, EquipmentSlot slot)
    {
        return CreateItem(itemId, placement: ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, slot)));
    }

    private static CharacterItem CreateItem(uint itemId, ItemPlacement? placement = null, ItemLifetime? lifetime = null)
    {
        return new CharacterItem(itemId, CharacterId, ItemTypeId, placement ?? ItemPlacement.CreateInventory(), 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, 0, null, 0, 0, 0, 1, lifetime ?? ItemLifetime.CreatePermanent());
    }
}
