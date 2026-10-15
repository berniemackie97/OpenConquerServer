using System.Diagnostics;
using OpenConquer.GameServer.World.Chat;

namespace OpenConquer.GameServer.Tests.World.Chat;

public sealed class WorldChatRateLimiterTests
{
    [Fact]
    public void FourMessagesAreAdmittedAndTheFifthMustWait()
    {
        WorldChatRateLimiter limiter = new();
        for (int i = 0; i < WorldChatRateLimiter.MessagesPerSecond; i++)
        {
            Assert.True(limiter.TryAccept(100, out TimeSpan delay));
            Assert.Equal(TimeSpan.Zero, delay);
        }

        Assert.False(limiter.TryAccept(101, out TimeSpan retry));
        Assert.True(retry > TimeSpan.Zero);
        Assert.True(limiter.TryAccept(100 + Stopwatch.Frequency, out _));
    }

    [Fact]
    public void ClockRegressionDoesNotResetBudget()
    {
        WorldChatRateLimiter limiter = new();
        Assert.True(limiter.TryAccept(200, out _));
        Assert.False(limiter.TryAccept(199, out TimeSpan retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    [Fact]
    public void SessionsHaveIndependentBudgets()
    {
        WorldChatRateLimiter first = new();
        WorldChatRateLimiter second = new();
        for (int i = 0; i < WorldChatRateLimiter.MessagesPerSecond; i++)
        {
            Assert.True(first.TryAccept(100, out _));
        }

        Assert.False(first.TryAccept(100, out _));
        Assert.True(second.TryAccept(100, out _));
    }
}
