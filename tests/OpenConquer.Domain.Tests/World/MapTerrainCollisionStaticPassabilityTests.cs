using OpenConquer.Domain.World;

namespace OpenConquer.Domain.Tests.World;

public sealed class MapTerrainCollisionStaticPassabilityTests
{
    [Fact]
    public void IsStaticallyPassable_BaseCellsRequireExactlyZeroFlag()
    {
        MapBaseTerrain terrain = new(1010, 3, 1,
        [
            new MapBaseTerrainCell(10, 0, 0),
            new MapBaseTerrainCell(10, 1, 0),
            new MapBaseTerrainCell(10, 2, 0),
        ], []);

        MapTerrainCollision collision = new(terrain, []);

        Assert.True(collision.IsStaticallyPassable(0, 0));
        Assert.False(collision.IsStaticallyPassable(1, 0));
        Assert.False(collision.IsStaticallyPassable(2, 0));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    public void IsStaticallyPassable_OutOfBoundsFailsClosed(int x, int y)
    {
        MapTerrainCollision collision = CreateCollision();

        Assert.False(collision.IsStaticallyPassable(x, y));
    }

    [Fact]
    public void IsStaticallyPassable_ComposedSceneryCanBlockOriginallyOpenCell()
    {
        MapBaseTerrain terrain = new(1010, 2, 1,
        [
            new MapBaseTerrainCell(10, 0, 0),
            new MapBaseTerrainCell(10, 1, 0),
        ], []);

        MapTerrainCollision collision = new(terrain,
        [
            new MapTerrainCollisionOverride(0, new MapBaseTerrainCell(20, 1, 0)),
        ]);

        Assert.False(collision.IsStaticallyPassable(0, 0));
        Assert.False(collision.IsStaticallyPassable(1, 0));
        Assert.Equal((ushort)0, terrain.GetCell(0, 0).PassabilityFlag);
    }

    [Fact]
    public void IsStaticallyPassable_ComposedSceneryCanOpenOriginallyBlockedCell()
    {
        MapBaseTerrain terrain = new(1010, 2, 1,
        [
            new MapBaseTerrainCell(10, 1, 0),
            new MapBaseTerrainCell(10, 0, 0),
        ], []);

        MapTerrainCollision collision = new(terrain,
        [
            new MapTerrainCollisionOverride(0, new MapBaseTerrainCell(20, 0, 0)),
        ]);

        Assert.True(collision.IsStaticallyPassable(0, 0));
        Assert.True(collision.IsStaticallyPassable(1, 0));
        Assert.Equal((ushort)1, terrain.GetCell(0, 0).PassabilityFlag);
    }

    private static MapTerrainCollision CreateCollision()
    {
        MapBaseTerrain terrain = new(1010, 2, 1,
        [
            new MapBaseTerrainCell(10, 0, 0),
            new MapBaseTerrainCell(10, 0, 0),
        ], []);

        return new MapTerrainCollision(terrain, []);
    }
}
