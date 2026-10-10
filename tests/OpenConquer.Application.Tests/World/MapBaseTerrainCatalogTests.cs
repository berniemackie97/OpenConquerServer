using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class MapBaseTerrainCatalogTests
{
    [Fact]
    public void Constructor_ValidTerrain_CreatesIndexedCatalog()
    {
        MapBaseTerrain first = CreateTerrain(1000);
        MapBaseTerrain second = CreateTerrain(1001);

        MapBaseTerrainCatalog catalog = new([second, first]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1000, out MapBaseTerrain? resolvedFirst));
        Assert.True(catalog.TryGet(1001, out MapBaseTerrain? resolvedSecond));
        Assert.Same(first, resolvedFirst);
        Assert.Same(second, resolvedSecond);
    }

    [Fact]
    public void Terrain_ConstructorCopiesMutableSourceArrays()
    {
        MapBaseTerrainCell[] sourceCells = [new(4, 5, -6)];
        MapTerrainExit[] sourceExits = [new(7, 8, 9)];

        MapBaseTerrain terrain = new(1000, 1, 1, sourceCells, sourceExits);

        sourceCells[0] = default;
        sourceExits[0] = default;

        Assert.Equal(new MapBaseTerrainCell(4, 5, -6), terrain.GetCell(0, 0));
        Assert.Equal(new MapTerrainExit(7, 8, 9), Assert.Single(terrain.Exits));
    }

    [Fact]
    public void Terrain_InvalidDimensionsOrCellCount_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapBaseTerrain(0, 1, 1, [default(MapBaseTerrainCell)], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapBaseTerrain(1000, 0, 1, [], []));
        Assert.Throws<ArgumentException>(() => new MapBaseTerrain(1000, 2, 2, [default(MapBaseTerrainCell)], []));
    }

    [Fact]
    public void Catalog_DuplicateTerrainId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapBaseTerrainCatalog([CreateTerrain(1000), CreateTerrain(1000)]));
    }

    [Fact]
    public void Catalog_EmptyDefinitions_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapBaseTerrainCatalog([]));
    }

    [Fact]
    public void Catalog_UnknownTerrain_ReturnsFalse()
    {
        MapBaseTerrainCatalog catalog = new([CreateTerrain(1000)]);

        Assert.False(catalog.TryGet(2000, out MapBaseTerrain? terrain));
        Assert.Null(terrain);
    }

    private static MapBaseTerrain CreateTerrain(uint mapDataId) =>
        new(mapDataId, 1, 1, [new MapBaseTerrainCell(1, 0, 0)], []);
}
