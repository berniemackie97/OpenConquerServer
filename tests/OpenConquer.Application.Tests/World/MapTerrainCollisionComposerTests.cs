using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.Tests.World;

public sealed class MapTerrainCollisionComposerTests
{
    [Fact]
    public void Compose_OrderedAttachments_ReplaceSurfaceAndAccumulateElevation()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 1, 200));

        MapTerrainAttachment[] attachments =
        [
            new(0, 0, 30, 1, 20),
            new(0, 0, 40, 0, -5)
        ];

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(terrain, attachments, 10);

        Assert.Equal(1000u, collision.MapDataId);
        Assert.Equal(1, collision.OverrideCount);
        Assert.Equal(new MapBaseTerrainCell(40, 0, 115), collision.GetEffectiveCell(0, 0));
        Assert.Equal(new MapBaseTerrainCell(20, 1, 200), collision.GetEffectiveCell(1, 0));
    }

    [Fact]
    public void Compose_ElevationOverflow_UsesSignedSixteenBitWrapping()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, short.MaxValue),
            new MapBaseTerrainCell(20, 0, 0));

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(terrain,
        [
            new MapTerrainAttachment(0, 0, 30, 1, 1)
        ], 10);

        Assert.Equal(short.MinValue, collision.GetEffectiveCell(0, 0).Elevation);
    }

    [Fact]
    public void Compose_AttachmentsOutsideGrid_DoNotCreateOverrides()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(terrain,
        [
            new MapTerrainAttachment(-1, 0, 30, 1, 10),
            new MapTerrainAttachment(2, 0, 30, 1, 10),
            new MapTerrainAttachment(0, -1, 30, 1, 10),
            new MapTerrainAttachment(0, 1, 30, 1, 10)
        ], 10);

        Assert.Equal(0, collision.OverrideCount);
    }

    [Fact]
    public void Compose_FinalCellEqualToBase_DoesNotStoreRedundantOverride()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(terrain,
        [
            new MapTerrainAttachment(0, 0, 30, 1, 10),
            new MapTerrainAttachment(0, 0, 10, 0, -10)
        ], 10);

        Assert.Equal(0, collision.OverrideCount);
        Assert.Equal(terrain.GetCell(0, 0), collision.GetEffectiveCell(0, 0));
    }

    [Fact]
    public void Compose_ExcessiveAttachmentCount_IsRejected()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        Assert.Throws<InvalidDataException>(() =>
            MapTerrainCollisionComposer.Compose(terrain,
            [
                new MapTerrainAttachment(0, 0, 30, 1, 0),
                new MapTerrainAttachment(1, 0, 40, 1, 0)
            ], 1));
    }

    [Fact]
    public void Compose_OverridesHaveDeterministicCellIndexOrder()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(terrain,
        [
            new MapTerrainAttachment(1, 0, 40, 1, 0),
            new MapTerrainAttachment(0, 0, 30, 1, 0)
        ], 10);

        Assert.Equal(new[] { 0, 1 },
            collision.Overrides.Select(entry => entry.CellIndex).ToArray());
    }

    [Fact]
    public void Collision_DuplicateOverride_IsRejected()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        MapTerrainCollisionOverride[] overrides =
        [
            new(0, new MapBaseTerrainCell(30, 1, 100)),
            new(0, new MapBaseTerrainCell(40, 1, 100))
        ];

        Assert.Throws<ArgumentException>(() => new MapTerrainCollision(terrain, overrides));
    }

    [Fact]
    public void Collision_OutOfBoundsLookup_IsRejected()
    {
        MapBaseTerrain terrain = CreateTerrain(
            new MapBaseTerrainCell(10, 0, 100),
            new MapBaseTerrainCell(20, 0, 200));

        MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(
            terrain, [], 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => collision.GetEffectiveCell(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => collision.GetEffectiveCell(-1, 0));
    }

    private static MapBaseTerrain CreateTerrain(params MapBaseTerrainCell[] cells)
    {
        return new MapBaseTerrain(1000, cells.Length, 1, cells, []);
    }
}
