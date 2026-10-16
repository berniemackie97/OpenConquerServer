using OpenConquer.Domain.World;

namespace OpenConquer.Domain.Tests.World;

public sealed class MapDistanceTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 3, 4, 5)]
    [InlineData(0, 0, 16, 4, 16)]
    [InlineData(0, 0, 16, 5, 17)]
    [InlineData(0, 0, 2, 2, 3)]
    [InlineData(0, 0, 1, 2, 2)]
    public void RoundedTileDistance_MatchesNativeModeOne(int ax, int ay, int bx, int by, int expected)
    {
        Assert.Equal(expected, MapDistance.RoundedTileDistance(ax, ay, bx, by));
        Assert.Equal(expected, MapDistance.RoundedTileDistance(bx, by, ax, ay));
    }
}
