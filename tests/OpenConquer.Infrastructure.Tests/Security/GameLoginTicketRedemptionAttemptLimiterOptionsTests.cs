using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketRedemptionAttemptLimiterOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new();

        Assert.Equal(30, options.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RequestWindow);
        Assert.Equal(4, options.MaximumConcurrentAttemptsPerSource);
        Assert.Equal(1, options.MaximumConcurrentAttemptsPerSession);
        Assert.Equal(8, options.FailedAttemptLimitPerSession);
        Assert.Equal(TimeSpan.FromMinutes(5), options.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), options.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(10), options.EntryRetention);
        Assert.Equal(512, options.MaximumConcurrentAttempts);
        Assert.Equal(100_000, options.MaximumTrackedEntries);
    }

    [Fact]
    public void Constructor_CustomValuesArePreserved()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            requestLimitPerSource: 60,
            requestWindow: TimeSpan.FromMinutes(2),
            maximumConcurrentAttemptsPerSource: 8,
            maximumConcurrentAttemptsPerSession: 2,
            failedAttemptLimitPerSession: 12,
            failureWindow: TimeSpan.FromMinutes(3),
            failureLockout: TimeSpan.FromMinutes(4),
            entryRetention: TimeSpan.FromMinutes(6),
            maximumConcurrentAttempts: 256,
            maximumTrackedEntries: 50_000
        );

        Assert.Equal(60, options.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RequestWindow);
        Assert.Equal(8, options.MaximumConcurrentAttemptsPerSource);
        Assert.Equal(2, options.MaximumConcurrentAttemptsPerSession);
        Assert.Equal(12, options.FailedAttemptLimitPerSession);
        Assert.Equal(TimeSpan.FromMinutes(3), options.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(4), options.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(6), options.EntryRetention);
        Assert.Equal(256, options.MaximumConcurrentAttempts);
        Assert.Equal(50_000, options.MaximumTrackedEntries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Constructor_InvalidRequestLimitThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(requestLimitPerSource: value)
        );
    }

    [Fact]
    public void Constructor_InvalidRequestWindowThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(requestWindow: TimeSpan.Zero)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(requestWindow: TimeSpan.FromHours(2))
        );
    }

    [Fact]
    public void Constructor_PerSourceConcurrencyCannotExceedGlobalConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(
                maximumConcurrentAttemptsPerSource: 5,
                maximumConcurrentAttempts: 4
            )
        );
    }

    [Fact]
    public void Constructor_PerSessionConcurrencyCannotExceedGlobalConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(
                maximumConcurrentAttemptsPerSession: 5,
                maximumConcurrentAttempts: 4
            )
        );
    }

    [Fact]
    public void Constructor_TrackedCapacityMustHoldSourceAndSession()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(maximumTrackedEntries: 1)
        );
    }

    [Fact]
    public void Constructor_EntryRetentionCannotExpireSecurityStateEarly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GameLoginTicketRedemptionAttemptLimiterOptions(
                requestWindow: TimeSpan.FromMinutes(6),
                failureWindow: TimeSpan.FromMinutes(7),
                failureLockout: TimeSpan.FromMinutes(8),
                entryRetention: TimeSpan.FromMinutes(7)
            )
        );
    }
}
