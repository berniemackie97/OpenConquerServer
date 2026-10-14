using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldAvatarActionLimiterTests
{
    [Fact]
    public void OneSessionCannotFanOutUnboundedCosmeticUpdates()
    {
        WorldAvatarActionLimiter limiter = new();
        for (int i = 0; i < WorldAvatarActionLimiter.MaximumActionsPerWindow; i++)
        {
            Assert.True(limiter.TryAccept(100));
        }

        Assert.False(limiter.TryAccept(101));
        Assert.False(limiter.TryAccept(100 + Stopwatch.Frequency - 1));
        Assert.True(limiter.TryAccept(100 + Stopwatch.Frequency));
    }

    [Fact]
    public void FailedClockRegressionDoesNotMoveWindowBackwards()
    {
        WorldAvatarActionLimiter limiter = new();
        Assert.True(limiter.TryAccept(200));
        Assert.False(limiter.TryAccept(199));
        Assert.True(limiter.TryAccept(201));
    }

    [Fact]
    public void WindowsArePerSessionNotShared()
    {
        WorldAvatarActionLimiter one = new();
        WorldAvatarActionLimiter other = new();
        for (int i = 0; i < WorldAvatarActionLimiter.MaximumActionsPerWindow; i++)
        {
            Assert.True(one.TryAccept(0));
        }
        Assert.False(one.TryAccept(1));
        Assert.True(other.TryAccept(1));
    }
}
