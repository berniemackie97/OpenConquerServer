using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Items.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Resolution;

public sealed class CharacterItemTypeValidatorTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint ItemTypeId = 100_000;

    [Fact]
    public void Validate_MatchingPermanentItemWithoutStaticLifetime_IsAccepted()
    {
        CharacterItem item = CreateItem();
        ItemTypeDefinition itemType = CreateItemType();

        CharacterItemTypeValidator.Validate(item, itemType);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    public void Validate_StackAtEffectiveCapacity_IsAccepted(int rawCapacity, int stackQuantity)
    {
        CharacterItem item = CreateItem(stackQuantity: checked((ushort)stackQuantity));
        ItemTypeDefinition itemType = CreateItemType(stackCapacity: checked((ushort)rawCapacity));

        CharacterItemTypeValidator.Validate(item, itemType);
    }

    [Fact]
    public void Validate_MismatchedDefinition_IsRejected()
    {
        CharacterItem item = CreateItem();
        ItemTypeDefinition itemType = CreateItemType(itemTypeId: ItemTypeId + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Equal("itemType", exception.ParamName);
    }

    [Fact]
    public void Validate_StackAboveEffectiveCapacity_FailsClosed()
    {
        CharacterItem item = CreateItem(stackQuantity: 2);
        ItemTypeDefinition itemType = CreateItemType(stackCapacity: 0);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Contains("stack quantity 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("capacity 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PermanentItemWithPositiveStaticLifetime_FailsClosed()
    {
        CharacterItem item = CreateItem();
        ItemTypeDefinition itemType = CreateItemType(staticLifetimeMinutes: 5);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Contains("positive static lifetime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PendingItemWithMatchingStaticLifetime_IsAccepted()
    {
        CharacterItem item = CreateItem(lifetime: ItemLifetime.CreatePendingActivation(300));
        ItemTypeDefinition itemType = CreateItemType(staticLifetimeMinutes: 5);

        CharacterItemTypeValidator.Validate(item, itemType);
    }

    [Fact]
    public void Validate_PendingItemWithoutStaticLifetime_FailsClosed()
    {
        CharacterItem item = CreateItem(lifetime: ItemLifetime.CreatePendingActivation(300));
        ItemTypeDefinition itemType = CreateItemType();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Contains("without a positive static lifetime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PendingDurationMismatch_FailsClosed()
    {
        CharacterItem item = CreateItem(lifetime: ItemLifetime.CreatePendingActivation(299));
        ItemTypeDefinition itemType = CreateItemType(staticLifetimeMinutes: 5);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_StaticLifetimeOverflow_FailsClosed()
    {
        CharacterItem item = CreateItem(lifetime: ItemLifetime.CreatePendingActivation(int.MaxValue));
        ItemTypeDefinition itemType = CreateItemType(staticLifetimeMinutes: uint.MaxValue);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => CharacterItemTypeValidator.Validate(item, itemType));

        Assert.Contains("cannot be represented in seconds", exception.Message, StringComparison.Ordinal);
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void Validate_ActiveExpiryDoesNotRequirePositiveStaticLifetime()
    {
        CharacterItem item = CreateItem(lifetime: ItemLifetime.CreateActiveExpiry(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        ItemTypeDefinition itemType = CreateItemType();

        CharacterItemTypeValidator.Validate(item, itemType);
    }

    private static CharacterItem CreateItem(ushort stackQuantity = 1, ItemLifetime? lifetime = null)
    {
        return new CharacterItem(1, CharacterId, ItemTypeId, ItemPlacement.CreateInventory(), 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            false, 0, null, 0, 0, 0, stackQuantity, lifetime ?? ItemLifetime.CreatePermanent());
    }

    private static ItemTypeDefinition CreateItemType(uint itemTypeId = ItemTypeId, uint staticLifetimeMinutes = 0, ushort stackCapacity = 1)
    {
        return new ItemTypeDefinition(itemTypeId, $"Item{itemTypeId}", 0, 0, 0, 0, 0, 0, staticLifetimeMinutes, stackCapacity);
    }
}
