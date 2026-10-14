using System.Diagnostics;
using OpenConquer.GameServer.World.Movement;

namespace OpenConquer.GameServer.Tests.World.Movement;

public sealed class WorldAvatarActionLimiterTests
{
    [Fact]
    public void ExhaustedWindowRequiresWaitingRatherThanDroppingFinalChange()
    {
        WorldAvatarActionLimiter limiter = new();
        for (int i = 0; i < WorldAvatarActionLimiter.MaximumActionsPerWindow; i++)
        {
            Assert.True(limiter.TryAccept(100, out TimeSpan acceptedDelay));
            Assert.Equal(TimeSpan.Zero, acceptedDelay);
        }

        Assert.False(limiter.TryAccept(101, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(retryAfter <= TimeSpan.FromSeconds(1));
        Assert.False(limiter.TryAccept(100 + Stopwatch.Frequency - 1, out TimeSpan boundaryDelay));
        Assert.True(boundaryDelay > TimeSpan.Zero);
        Assert.True(limiter.TryAccept(100 + Stopwatch.Frequency, out TimeSpan nextWindowDelay));
        Assert.Equal(TimeSpan.Zero, nextWindowDelay);
    }

    [Fact]
    public void ClockRegressionDoesNotResetWindow()
    {
        WorldAvatarActionLimiter limiter = new();
        Assert.True(limiter.TryAccept(200, out _));
        Assert.False(limiter.TryAccept(199, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(limiter.TryAccept(201, out _));
    }

    [Fact]
    public void WindowsArePerSessionNotShared()
    {
        WorldAvatarActionLimiter one = new();
        WorldAvatarActionLimiter other = new();
        for (int i = 0; i < WorldAvatarActionLimiter.MaximumActionsPerWindow; i++)
        {
            Assert.True(one.TryAccept(0, out _));
        }
        Assert.False(one.TryAccept(1, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(other.TryAccept(1, out TimeSpan otherDelay));
        Assert.Equal(TimeSpan.Zero, otherDelay);
    }

    [Fact]
    public void EveryQueuedActionEventuallyReceivesAnAdmissionSlot()
    {
        WorldAvatarActionLimiter limiter = new();
        long timestamp = 0;
        int accepted = 0;
        int waits = 0;
        while (accepted < 25)
        {
            if (limiter.TryAccept(timestamp, out TimeSpan retryAfter))
            {
                accepted++;
                continue;
            }

            waits++;
            Assert.True(retryAfter > TimeSpan.Zero);
            timestamp += checked((long)Math.Ceiling(retryAfter.TotalSeconds * Stopwatch.Frequency));
        }

        Assert.Equal(25, accepted);
        Assert.Equal(3, waits);
    }
}
