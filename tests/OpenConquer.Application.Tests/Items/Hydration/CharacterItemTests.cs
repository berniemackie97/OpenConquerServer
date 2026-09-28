using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Hydration;

public sealed class CharacterItemTests
{
    [Fact]
    public void Constructor_ValidInventoryItem_PreservesCompleteHydratedState()
    {
        ItemPlacement placement = ItemPlacement.CreateInventory();
        DateTimeOffset expiresAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ItemLifetime lifetime = ItemLifetime.CreateActiveExpiry(expiresAtUtc);

        CharacterItem item = new(
            itemId: 1001,
            ownerCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
            itemTypeId: 410_339,
            placement,
            durability: 1200,
            maximumDurability: 1500,
            retailCompatibilityByteA: 1,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 123456,
            socket1Code: 13,
            socket2Code: 14,
            hiddenAttackEffect: 987654,
            retailCompatibilityByteB: 2,
            additionLevel: 12,
            damageReductionPercentOrSteedCompositionRed: 7,
            itemBindingCode: 3,
            enchantmentLifeBonusOrSteedCompositionGreen: 25,
            monsterRestraintIdOrSteedCompositionBlue: 456789,
            isSuspicious: true,
            equipmentLockStateMask: 0,
            equipmentUnlockAtUtc: null,
            equipmentColor: 5,
            compositionProgress: 12345,
            inscribedSyndicateId: 67890,
            stackQuantity: 20,
            lifetime);

        Assert.Equal(1001u, item.ItemId);
        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, item.OwnerCharacterId);
        Assert.Equal(410_339u, item.ItemTypeId);
        Assert.Equal(placement, item.Placement);
        Assert.Equal((ushort)1200, item.Durability);
        Assert.Equal((ushort)1500, item.MaximumDurability);
        Assert.Equal((byte)1, item.RetailCompatibilityByteA);
        Assert.Equal(123456u, item.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline);
        Assert.Equal((byte)13, item.Socket1Code);
        Assert.Equal((byte)14, item.Socket2Code);
        Assert.Equal(987654u, item.HiddenAttackEffect);
        Assert.Equal((byte)2, item.RetailCompatibilityByteB);
        Assert.Equal((byte)12, item.AdditionLevel);
        Assert.Equal((byte)7, item.DamageReductionPercentOrSteedCompositionRed);
        Assert.Equal((byte)3, item.ItemBindingCode);
        Assert.Equal((byte)25, item.EnchantmentLifeBonusOrSteedCompositionGreen);
        Assert.Equal(456789u, item.MonsterRestraintIdOrSteedCompositionBlue);
        Assert.True(item.IsSuspicious);
        Assert.Equal((ushort)0, item.EquipmentLockStateMask);
        Assert.Null(item.EquipmentUnlockAtUtc);
        Assert.Equal((ushort)5, item.EquipmentColor);
        Assert.Equal(12345u, item.CompositionProgress);
        Assert.Equal(67890u, item.InscribedSyndicateId);
        Assert.Equal((ushort)20, item.StackQuantity);
        Assert.Equal(lifetime, item.Lifetime);
    }

    [Fact]
    public void Constructor_ValidEquipmentItem_IsAccepted()
    {
        EquipmentPosition position = EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.RightHand);
        ItemPlacement placement = ItemPlacement.CreateEquipment(position);

        CharacterItem item = CreateItem(placement: placement);

        Assert.Equal(placement, item.Placement);
        Assert.True(item.Placement.IsEquipment);
    }

    [Fact]
    public void Constructor_FirstPlayerEntityId_IsAccepted()
    {
        CharacterItem item = CreateItem(ownerCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId);

        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, item.OwnerCharacterId);
    }

    [Fact]
    public void Constructor_PendingUnlockWithUtcSchedule_IsAccepted()
    {
        DateTimeOffset unlockAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        CharacterItem item = CreateItem(
            equipmentLockStateMask: 0x8002,
            equipmentUnlockAtUtc: unlockAtUtc);

        Assert.Equal((ushort)0x8002, item.EquipmentLockStateMask);
        Assert.Equal(unlockAtUtc, item.EquipmentUnlockAtUtc);
    }

    [Fact]
    public void Constructor_ZeroItemId_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateItem(itemId: 0));

        Assert.Equal("itemId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_NonPlayerOwnerCharacterId_ThrowsArgumentOutOfRangeException(uint ownerCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateItem(ownerCharacterId: ownerCharacterId));

        Assert.Equal("ownerCharacterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_ZeroItemTypeId_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateItem(itemTypeId: 0));

        Assert.Equal("itemTypeId", exception.ParamName);
    }

    [Fact]
    public void Constructor_InvalidPlacement_ThrowsArgumentException()
    {
        ItemPlacement invalidPlacement = default;

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateItem(placement: invalidPlacement));

        Assert.Equal("placement", exception.ParamName);
    }

    [Fact]
    public void Constructor_ZeroStackQuantity_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateItem(stackQuantity: 0));

        Assert.Equal("stackQuantity", exception.ParamName);
    }

    [Fact]
    public void Constructor_PendingUnlockWithoutSchedule_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateItem(equipmentLockStateMask: 2));

        Assert.Equal("equipmentUnlockAtUtc", exception.ParamName);
    }

    [Fact]
    public void Constructor_UnlockScheduleWithoutPendingState_ThrowsArgumentException()
    {
        DateTimeOffset unlockAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateItem(equipmentUnlockAtUtc: unlockAtUtc));

        Assert.Equal("equipmentUnlockAtUtc", exception.ParamName);
    }

    [Fact]
    public void Constructor_NonUtcUnlockSchedule_ThrowsArgumentException()
    {
        DateTimeOffset unlockAtUtc = new(2027, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5));

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateItem(
                equipmentLockStateMask: 2,
                equipmentUnlockAtUtc: unlockAtUtc));

        Assert.Equal("equipmentUnlockAtUtc", exception.ParamName);
    }

    [Fact]
    public void Constructor_InvalidLifetime_ThrowsArgumentException()
    {
        ItemLifetime invalidLifetime = default;

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateItem(lifetime: invalidLifetime));

        Assert.Equal("lifetime", exception.ParamName);
    }

    private static CharacterItem CreateItem(uint itemId = 1, uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId,
        uint itemTypeId = 100_000, ItemPlacement? placement = null, ushort equipmentLockStateMask = 0,
        DateTimeOffset? equipmentUnlockAtUtc = null, ushort stackQuantity = 1, ItemLifetime? lifetime = null)
    {
        return new CharacterItem(
            itemId,
            ownerCharacterId,
            itemTypeId,
            placement ?? ItemPlacement.CreateInventory(),
            durability: 100,
            maximumDurability: 100,
            retailCompatibilityByteA: 0,
            talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 0,
            socket1Code: 0,
            socket2Code: 0,
            hiddenAttackEffect: 0,
            retailCompatibilityByteB: 0,
            additionLevel: 0,
            damageReductionPercentOrSteedCompositionRed: 0,
            itemBindingCode: 0,
            enchantmentLifeBonusOrSteedCompositionGreen: 0,
            monsterRestraintIdOrSteedCompositionBlue: 0,
            isSuspicious: false,
            equipmentLockStateMask,
            equipmentUnlockAtUtc,
            equipmentColor: 0,
            compositionProgress: 0,
            inscribedSyndicateId: 0,
            stackQuantity,
            lifetime ?? ItemLifetime.CreatePermanent());
    }
}
