using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class AccountAuthenticationProtectionOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        AccountAuthenticationProtectionOptions options = new();

        Assert.Equal(30, options.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RequestWindow);
        Assert.Equal(4, options.MaximumConcurrentRequestsPerSource);
        Assert.Equal(32, options.MaximumConcurrentRequests);
        Assert.Equal(2, options.MaximumConcurrentAttemptsPerAccount);
        Assert.Equal(8, options.FailedAttemptLimitPerAccountSource);
        Assert.Equal(TimeSpan.FromMinutes(5), options.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), options.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(10), options.EntryRetention);
        Assert.Equal(100_000, options.MaximumTrackedEntries);
    }

    [Fact]
    public void Constructor_CustomValuesArePreserved()
    {
        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 60,
            requestWindow: TimeSpan.FromMinutes(2),
            maximumConcurrentRequestsPerSource: 8,
            maximumConcurrentRequests: 64,
            maximumConcurrentAttemptsPerAccount: 3,
            failedAttemptLimitPerAccountSource: 12,
            failureWindow: TimeSpan.FromMinutes(3),
            failureLockout: TimeSpan.FromMinutes(4),
            entryRetention: TimeSpan.FromMinutes(6),
            maximumTrackedEntries: 50_000
        );

        Assert.Equal(60, options.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RequestWindow);
        Assert.Equal(8, options.MaximumConcurrentRequestsPerSource);
        Assert.Equal(64, options.MaximumConcurrentRequests);
        Assert.Equal(3, options.MaximumConcurrentAttemptsPerAccount);
        Assert.Equal(12, options.FailedAttemptLimitPerAccountSource);
        Assert.Equal(TimeSpan.FromMinutes(3), options.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(4), options.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(6), options.EntryRetention);
        Assert.Equal(50_000, options.MaximumTrackedEntries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Constructor_InvalidRequestLimitThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(requestLimitPerSource: value)
        );
    }

    [Fact]
    public void Constructor_InvalidRequestWindowThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(requestWindow: TimeSpan.Zero)
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(requestWindow: TimeSpan.FromHours(2))
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_025)]
    public void Constructor_InvalidPerSourceConcurrencyThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(maximumConcurrentRequestsPerSource: value)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Constructor_InvalidGlobalConcurrencyThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(maximumConcurrentRequests: value)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_025)]
    public void Constructor_InvalidPerAccountConcurrencyThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(maximumConcurrentAttemptsPerAccount: value)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Constructor_InvalidFailureLimitThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(failedAttemptLimitPerAccountSource: value)
        );
    }

    [Fact]
    public void Constructor_InvalidFailureWindowThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(failureWindow: TimeSpan.Zero)
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(failureWindow: TimeSpan.FromDays(2))
        );
    }

    [Fact]
    public void Constructor_InvalidFailureLockoutThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(failureLockout: TimeSpan.Zero)
        );

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(failureLockout: TimeSpan.FromDays(2))
        );
    }

    [Fact]
    public void Constructor_PerSourceConcurrencyCannotExceedGlobalConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(
                maximumConcurrentRequestsPerSource: 5,
                maximumConcurrentRequests: 4
            )
        );
    }

    [Fact]
    public void Constructor_PerAccountConcurrencyCannotExceedGlobalConcurrency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(
                maximumConcurrentAttemptsPerAccount: 5,
                maximumConcurrentRequests: 4
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2_000_001)]
    public void Constructor_InvalidTrackedCapacityThrows(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(maximumTrackedEntries: value)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Constructor_TrackedCapacityMustHoldSourceAccountAndAccountSource(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(maximumTrackedEntries: value)
        );
    }

    [Fact]
    public void Constructor_EntryRetentionCannotExpireSecurityStateEarly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationProtectionOptions(
                requestWindow: TimeSpan.FromMinutes(6),
                failureWindow: TimeSpan.FromMinutes(7),
                failureLockout: TimeSpan.FromMinutes(8),
                entryRetention: TimeSpan.FromMinutes(7)
            )
        );
    }

    [Fact]
    public void Constructor_MaximumSupportedBoundaryValuesAreAccepted()
    {
        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 1_000_000,
            requestWindow: TimeSpan.FromHours(1),
            maximumConcurrentRequestsPerSource: 1_024,
            maximumConcurrentRequests: 1_024,
            maximumConcurrentAttemptsPerAccount: 1_024,
            failedAttemptLimitPerAccountSource: 1_000_000,
            failureWindow: TimeSpan.FromDays(1),
            failureLockout: TimeSpan.FromDays(1),
            entryRetention: TimeSpan.FromDays(7),
            maximumTrackedEntries: 2_000_000
        );

        Assert.Equal(1_000_000, options.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromHours(1), options.RequestWindow);
        Assert.Equal(1_024, options.MaximumConcurrentRequestsPerSource);
        Assert.Equal(1_024, options.MaximumConcurrentRequests);
        Assert.Equal(1_024, options.MaximumConcurrentAttemptsPerAccount);
        Assert.Equal(1_000_000, options.FailedAttemptLimitPerAccountSource);
        Assert.Equal(TimeSpan.FromDays(1), options.FailureWindow);
        Assert.Equal(TimeSpan.FromDays(1), options.FailureLockout);
        Assert.Equal(TimeSpan.FromDays(7), options.EntryRetention);
        Assert.Equal(2_000_000, options.MaximumTrackedEntries);
    }
}
