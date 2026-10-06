using OpenConquer.Application.Items.Catalog;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Tests.Items.Catalog;

public sealed class ItemTypeCatalogTests
{
    [Fact]
    public void Constructor_ValidDefinitions_CreatesIndexedCatalog()
    {
        ItemTypeDefinition first = CreateDefinition(100001);
        ItemTypeDefinition second = CreateDefinition(100002);

        ItemTypeCatalog catalog = new([second, first]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(first.ItemTypeId, out ItemTypeDefinition? resolvedFirst));
        Assert.True(catalog.TryGet(second.ItemTypeId, out ItemTypeDefinition? resolvedSecond));
        Assert.Same(first, resolvedFirst);
        Assert.Same(second, resolvedSecond);
    }

    [Fact]
    public void TryGet_UnknownItemType_ReturnsFalse()
    {
        ItemTypeCatalog catalog = new([CreateDefinition(100001)]);

        Assert.False(catalog.TryGet(999999, out ItemTypeDefinition? definition));
        Assert.Null(definition);
    }

    [Fact]
    public void TryGet_ZeroItemType_ReturnsFalse()
    {
        ItemTypeCatalog catalog = new([CreateDefinition(100001)]);

        Assert.False(catalog.TryGet(0, out ItemTypeDefinition? definition));
        Assert.Null(definition);
    }

    [Fact]
    public void Constructor_NullDefinitions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ItemTypeCatalog(null!));
    }

    [Fact]
    public void Constructor_EmptyDefinitions_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ItemTypeCatalog([]));

        Assert.Equal("definitions", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullDefinition_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ItemTypeCatalog([CreateDefinition(100001), null!]));

        Assert.Equal("definitions", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateItemTypeId_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ItemTypeCatalog([CreateDefinition(100001), CreateDefinition(100001)]));

        Assert.Equal("definitions", exception.ParamName);
    }

    private static ItemTypeDefinition CreateDefinition(uint itemTypeId)
    {
        return new ItemTypeDefinition(
            itemTypeId,
            name: $"Item{itemTypeId}",
            requiredLevel: 0,
            speedPercentOffset: 0,
            life: 0,
            mana: 0,
            initialDurability: 1,
            maximumDurability: 1,
            staticLifetimeMinutes: 0,
            stackCapacity: 0);
    }
}
