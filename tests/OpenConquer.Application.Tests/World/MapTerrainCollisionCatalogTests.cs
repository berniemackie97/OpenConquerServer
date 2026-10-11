using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class MapTerrainCollisionCatalogTests
{
    [Fact]
    public void Constructor_ValidCollisions_CreatesIndexedCatalog()
    {
        MapTerrainCollision first = CreateCollision(1000);
        MapTerrainCollision second = CreateCollision(1001);

        MapTerrainCollisionCatalog catalog = new([second, first]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1000, out MapTerrainCollision? resolvedFirst));
        Assert.True(catalog.TryGet(1001, out MapTerrainCollision? resolvedSecond));
        Assert.Same(first, resolvedFirst);
        Assert.Same(second, resolvedSecond);
    }

    [Fact]
    public void Constructor_DuplicateMapDataId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapTerrainCollisionCatalog([CreateCollision(1000), CreateCollision(1000)]));
    }

    [Fact]
    public void Constructor_EmptyCatalog_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapTerrainCollisionCatalog([]));
    }

    [Fact]
    public void Constructor_NullCollision_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapTerrainCollisionCatalog([CreateCollision(1000), null!]));
    }

    [Fact]
    public void Constructor_NullCollection_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MapTerrainCollisionCatalog(null!));
    }

    [Fact]
    public void TryGet_UnknownMapDataId_ReturnsFalse()
    {
        MapTerrainCollisionCatalog catalog = new([CreateCollision(1000)]);

        Assert.False(catalog.TryGet(2000, out MapTerrainCollision? collision));
        Assert.Null(collision);
    }

    private static MapTerrainCollision CreateCollision(uint mapDataId)
    {
        MapBaseTerrain terrain = new(mapDataId, 1, 1, [new MapBaseTerrainCell(10, 0, 100)], []);
        return new MapTerrainCollision(terrain, []);
    }
}
