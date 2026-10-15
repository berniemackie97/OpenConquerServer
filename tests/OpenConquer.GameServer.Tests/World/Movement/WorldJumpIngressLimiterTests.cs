using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldJumpIngressLimiterTests
{
    [Fact]
    public void BoundsAllJumpIntentsIncludingRejectedTargetsWithinOneSecond()
    {
        WorldJumpIngressLimiter limiter = new();
        for (int attempt = 0; attempt < WorldJumpIngressLimiter.MaximumAttemptsPerSecond; attempt++)
        {
            Assert.True(limiter.TryAccept(1000));
        }
        Assert.False(limiter.TryAccept(1000 + Stopwatch.Frequency - 1));
        Assert.True(limiter.TryAccept(1000 + Stopwatch.Frequency));
    }

    [Fact]
    public void ClockRegressionResetsTheBoundedWindow()
    {
        WorldJumpIngressLimiter limiter = new();
        Assert.True(limiter.TryAccept(1000));
        Assert.True(limiter.TryAccept(999));
    }
}
