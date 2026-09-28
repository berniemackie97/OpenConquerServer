using OpenConquer.Application.Items.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Hydration;

public sealed class CharacterItemSetTests
{
    [Fact]
    public void Constructor_EmptyItemSet_IsAccepted()
    {
        CharacterItemSet itemSet = new(CharacterIdentityPolicy.FirstPlayerEntityId, []);

        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, itemSet.CharacterId);
        Assert.Empty(itemSet.Items);
        Assert.Equal(0, itemSet.Count);
    }

    [Fact]
    public void Constructor_UnorderedItems_SortsByItemId()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterItem[] items = [CreateItem(30, characterId), CreateItem(10, characterId), CreateItem(20, characterId)];

        CharacterItemSet itemSet = new(characterId, items);

        Assert.Equal([10u, 20u, 30u], itemSet.Items.Select(static item => item.ItemId));
        Assert.Equal(3, itemSet.Count);
    }

    [Fact]
    public void Constructor_CapturesItemsIndependentlyOfSourceCollection()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        List<CharacterItem> items = [CreateItem(1, characterId)];

        CharacterItemSet itemSet = new(characterId, items);
        items.Clear();

        Assert.Single(itemSet.Items);
        Assert.Equal(1u, itemSet.Items[0].ItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_NonPlayerCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterItemSet(characterId, []));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullItems_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new CharacterItemSet(CharacterIdentityPolicy.FirstPlayerEntityId, null!));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullItem_ThrowsArgumentException()
    {
        CharacterItem[] items = [null!];

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterItemSet(CharacterIdentityPolicy.FirstPlayerEntityId, items));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_ItemOwnedByDifferentCharacter_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterItem item = CreateItem(1, characterId + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterItemSet(characterId, [item]));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateItemId_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterItem first = CreateItem(1, characterId);
        CharacterItem second = CreateItem(1, characterId);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterItemSet(characterId, [first, second]));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateEquipmentPosition_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        ItemPlacement placement = ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.RightHand));
        CharacterItem first = CreateItem(1, characterId, placement);
        CharacterItem second = CreateItem(2, characterId, placement);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterItemSet(characterId, [first, second]));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_SameSlotInDifferentEquipmentSets_IsAccepted()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        ItemPlacement mainPlacement = ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Main, EquipmentSlot.Headwear));
        ItemPlacement alternatePlacement = ItemPlacement.CreateEquipment(EquipmentPosition.Create(EquipmentSet.Alternate, EquipmentSlot.Headwear));

        CharacterItemSet itemSet = new(characterId, [CreateItem(1, characterId, mainPlacement), CreateItem(2, characterId, alternatePlacement)]);

        Assert.Equal(2, itemSet.Count);
    }

    private static CharacterItem CreateItem(uint itemId, uint ownerCharacterId, ItemPlacement? placement = null)
    {
        return new CharacterItem(itemId, ownerCharacterId, itemTypeId: 100_000, placement ?? ItemPlacement.CreateInventory(),
            durability: 100, maximumDurability: 100, retailCompatibilityByteA: 0, talismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline: 0,
            socket1Code: 0, socket2Code: 0, hiddenAttackEffect: 0, retailCompatibilityByteB: 0, additionLevel: 0,
            damageReductionPercentOrSteedCompositionRed: 0, itemBindingCode: 0, enchantmentLifeBonusOrSteedCompositionGreen: 0,
            monsterRestraintIdOrSteedCompositionBlue: 0, isSuspicious: false, equipmentLockStateMask: 0, equipmentUnlockAtUtc: null,
            equipmentColor: 0, compositionProgress: 0, inscribedSyndicateId: 0, stackQuantity: 1, ItemLifetime.CreatePermanent());
    }
}
