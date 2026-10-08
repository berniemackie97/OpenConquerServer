using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class MapDefinitionCatalogTests
{
    [Fact]
    public void Constructor_ValidDefinitions_CreatesImmutableIndex()
    {
        MapDefinition first = new(1002, 1010, 0x0000_0040_0000_0000);
        MapDefinition second = new(1003, 1010, ulong.MaxValue);

        MapDefinitionCatalog catalog = new([second, first]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1002, out MapDefinition? resolvedFirst));
        Assert.True(catalog.TryGet(1003, out MapDefinition? resolvedSecond));
        Assert.Same(first, resolvedFirst);
        Assert.Same(second, resolvedSecond);
    }

    [Fact]
    public void Constructor_SharedMapDataId_IsAllowed()
    {
        MapDefinitionCatalog catalog = new([
            new MapDefinition(1002, 1010, 0),
            new MapDefinition(1003, 1010, 1),
        ]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1002, out MapDefinition? first));
        Assert.True(catalog.TryGet(1003, out MapDefinition? second));
        Assert.Equal(first.MapDataId, second.MapDataId);
        Assert.NotEqual(first.MapId, second.MapId);
    }

    [Fact]
    public void TryGet_UnknownMap_ReturnsFalse()
    {
        MapDefinitionCatalog catalog = new([new MapDefinition(1002, 1010, 0)]);

        Assert.False(catalog.TryGet(9999, out MapDefinition? definition));
        Assert.Null(definition);
        Assert.False(catalog.TryGet(0, out definition));
        Assert.Null(definition);
    }

    [Fact]
    public void Constructor_NullDefinitions_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MapDefinitionCatalog(null!));
    }

    [Fact]
    public void Constructor_EmptyDefinitions_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new MapDefinitionCatalog([]));

        Assert.Equal("definitions", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullDefinition_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new MapDefinitionCatalog([new MapDefinition(1002, 1010, 0), null!]));

        Assert.Equal("definitions", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateWorldMapId_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new MapDefinitionCatalog([
                new MapDefinition(1002, 1010, 0),
                new MapDefinition(1002, 1011, 1),
            ]));

        Assert.Equal("definitions", exception.ParamName);
    }
}
