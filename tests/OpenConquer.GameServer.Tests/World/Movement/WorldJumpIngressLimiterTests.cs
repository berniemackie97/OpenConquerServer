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
    public void ClockRegressionCannotReopenAnExhaustedWindow()
    {
        WorldJumpIngressLimiter limiter = new();
        long start = 1000;
        for (int attempt = 0; attempt < WorldJumpIngressLimiter.MaximumAttemptsPerSecond; attempt++)
        {
            Assert.True(limiter.TryAccept(start));
        }

        Assert.False(limiter.TryAccept(start - 1));
        Assert.False(limiter.TryAccept(start + Stopwatch.Frequency - 1));
        Assert.True(limiter.TryAccept(start + Stopwatch.Frequency));
    }

    [Fact]
    public void ClockRegressionDoesNotConsumeRemainingBudget()
    {
        WorldJumpIngressLimiter limiter = new();
        Assert.True(limiter.TryAccept(1000));
        Assert.False(limiter.TryAccept(999));
        for (int attempt = 1; attempt < WorldJumpIngressLimiter.MaximumAttemptsPerSecond; attempt++)
        {
            Assert.True(limiter.TryAccept(1000));
        }

        Assert.False(limiter.TryAccept(1000));
    }

    [Fact]
    public void RegressedTimestampWithinCurrentWindowDoesNotConsumeAnAttempt()
    {
        WorldJumpIngressLimiter limiter = new();
        Assert.True(limiter.TryAccept(1000));
        Assert.True(limiter.TryAccept(1200));
        Assert.False(limiter.TryAccept(1100));

        for (int attempt = 2; attempt < WorldJumpIngressLimiter.MaximumAttemptsPerSecond; attempt++)
        {
            Assert.True(limiter.TryAccept(1200));
        }

        Assert.False(limiter.TryAccept(1200));
    }

    [Fact]
    public void SessionsHaveIndependentAttemptBudgets()
    {
        WorldJumpIngressLimiter first = new();
        WorldJumpIngressLimiter second = new();
        for (int attempt = 0; attempt < WorldJumpIngressLimiter.MaximumAttemptsPerSecond; attempt++)
        {
            Assert.True(first.TryAccept(1000));
        }

        Assert.False(first.TryAccept(999));
        Assert.False(first.TryAccept(1000));
        Assert.True(second.TryAccept(999));
    }
}
