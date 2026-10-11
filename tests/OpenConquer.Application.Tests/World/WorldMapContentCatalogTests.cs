using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class WorldMapContentCatalogTests
{
    [Fact]
    public void Constructor_ValidMaps_PreservesWorldIdentityAndMetadata()
    {
        MapTerrainCollision collision = CreateCollision(500);
        WorldMapContent first = new(new MapDefinition(1000, 500, ulong.MaxValue), collision);
        WorldMapContent second = new(new MapDefinition(1001, 500, 0), collision);

        WorldMapContentCatalog catalog = new([second, first]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1000, out WorldMapContent? resolvedFirst));
        Assert.True(catalog.TryGet(1001, out WorldMapContent? resolvedSecond));
        Assert.Same(first, resolvedFirst);
        Assert.Same(second, resolvedSecond);
        Assert.Equal(500u, resolvedFirst.MapDataId);
        Assert.Equal(ulong.MaxValue, resolvedFirst.Flags);
        Assert.Same(resolvedFirst.Collision, resolvedSecond.Collision);
        Assert.Same(collision.BaseTerrain, resolvedFirst.BaseTerrain);
        Assert.Equal(2, catalog.Maps.Count());
    }

    [Fact]
    public void Constructor_DifferentCollisionForSharedTerrain_IsRejected()
    {
        WorldMapContent first = new(new MapDefinition(1000, 500, 0), CreateCollision(500));
        WorldMapContent second = new(new MapDefinition(1001, 500, 0), CreateCollision(500));

        Assert.Throws<ArgumentException>(() => new WorldMapContentCatalog([first, second]));
    }

    [Fact]
    public void Constructor_DuplicateWorldMap_IsRejected()
    {
        MapTerrainCollision collision = CreateCollision(500);

        Assert.Throws<ArgumentException>(() => new WorldMapContentCatalog([
            new WorldMapContent(new MapDefinition(1000, 500, 0), collision),
            new WorldMapContent(new MapDefinition(1000, 500, 1), collision)
        ]));
    }

    [Fact]
    public void Constructor_EmptyCatalog_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new WorldMapContentCatalog([]));
    }

    [Fact]
    public void Constructor_NullCollectionOrEntry_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new WorldMapContentCatalog(null!));
        Assert.Throws<ArgumentException>(() => new WorldMapContentCatalog([CreateMap(1000, 500), null!]));
    }

    [Fact]
    public void WorldMapContent_IncompatibleCollisionIdentity_IsRejected()
    {
        MapDefinition definition = new(1000, 500, 0);

        Assert.Throws<ArgumentException>(() => new WorldMapContent(definition, CreateCollision(501)));
    }

    [Fact]
    public void WorldMapContent_NullInputs_AreRejected()
    {
        MapDefinition definition = new(1000, 500, 0);
        MapTerrainCollision collision = CreateCollision(500);

        Assert.Throws<ArgumentNullException>(() => new WorldMapContent(null!, collision));
        Assert.Throws<ArgumentNullException>(() => new WorldMapContent(definition, null!));
    }

    [Fact]
    public void TryGet_UnknownMapId_ReturnsFalse()
    {
        WorldMapContentCatalog catalog = new([CreateMap(1000, 500)]);

        Assert.False(catalog.TryGet(500, out WorldMapContent? map));
        Assert.Null(map);
    }

    private static WorldMapContent CreateMap(uint mapId, uint mapDataId)
    {
        return new WorldMapContent(new MapDefinition(mapId, mapDataId, 0), CreateCollision(mapDataId));
    }

    private static MapTerrainCollision CreateCollision(uint mapDataId)
    {
        MapBaseTerrain terrain = new(mapDataId, 1, 1, [new MapBaseTerrainCell(10, 0, -5)], []);
        return new MapTerrainCollision(terrain, []);
    }
}
