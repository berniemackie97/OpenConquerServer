using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldPrimaryStatusMaskTests
{
    [Fact]
    public void NativePrimaryStatusBitsAreDistinctAndPreserveFullWidth()
    {
        WorldPrimaryStatusMask status = WorldPrimaryStatusMask.ForAdmission(alive: false).With(WorldPrimaryStatusMask.Ghost).With(111);
        Assert.True(status.Has(WorldPrimaryStatusMask.Dead));
        Assert.True(status.Has(WorldPrimaryStatusMask.Ghost));
        Assert.True(status.Has(111));
        Assert.Equal(1UL << 5 | 1UL << 10, status.Low);
        Assert.Equal(1UL << (111 - 64), status.High);
        Assert.Equal(default(WorldPrimaryStatusMask), WorldPrimaryStatusMask.ForAdmission(alive: true));
    }

    [Theory]
    [InlineData(1003u, 980001003u)]
    [InlineData(2003u, 990002003u)]
    public void GhostLookMatchesHistoricalTransformFamilies(uint baseLook, uint expected)
    {
        Assert.Equal(expected, WorldGhostAppearance.FromBaseLook(baseLook));
    }
}
