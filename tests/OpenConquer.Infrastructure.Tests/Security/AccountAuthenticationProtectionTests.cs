using System.Net;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class AccountAuthenticationProtectionTests
{
    private static readonly IPAddress s_address = IPAddress.Parse("192.0.2.10");

    [Fact]
    public void Constructor_NullOptionsThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticationProtection(null!, TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_NullTimeProviderThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AccountAuthenticationProtection(
                new AccountAuthenticationProtectionOptions(),
                null!
            )
        );
    }

    [Fact]
    public void TryBeginRequest_NullRemoteAddressThrows()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.Throws<ArgumentNullException>(() =>
            protection.TryBeginAuthentication(
                null!,
                out IAccountAuthenticationRequestLease? _
            )
        );
    }

    [Fact]
    public void TryBeginRequest_FirstRequestIsAdmitted()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? request
            )
        );

        Assert.NotNull(request);

        request.Dispose();
    }

    [Fact]
    public void TryBeginRequest_PerSourceConcurrencyLimitIsEnforced()
    {
        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequestsPerSource: 1
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? first
            )
        );

        Assert.False(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? second
            )
        );

        Assert.Null(second);

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public void TryBeginRequest_GlobalConcurrencyLimitIsEnforced()
    {
        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequestsPerSource: 2,
            maximumConcurrentRequests: 2
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.10"),
                out IAccountAuthenticationRequestLease? first
            )
        );

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.11"),
                out IAccountAuthenticationRequestLease? second
            )
        );

        Assert.False(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.12"),
                out IAccountAuthenticationRequestLease? third
            )
        );

        Assert.Null(third);

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.12"),
                out IAccountAuthenticationRequestLease? afterRelease
            )
        );

        second.Dispose();
        afterRelease.Dispose();
    }

    [Fact]
    public void TryBeginRequest_SourceRequestLimitIsConsumedByDisposedRequests()
    {
        ManualTimeProvider timeProvider = new();

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 2,
            requestWindow: TimeSpan.FromMinutes(1)
        );

        AccountAuthenticationProtection protection = CreateProtection(options, timeProvider);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? first
            )
        );

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? second
            )
        );

        second.Dispose();

        Assert.False(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? denied
            )
        );

        Assert.Null(denied);
    }

    [Fact]
    public void TryBeginRequest_SourceRequestLimitRefillsOverTime()
    {
        ManualTimeProvider timeProvider = new();

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 2,
            requestWindow: TimeSpan.FromMinutes(1)
        );

        AccountAuthenticationProtection protection = CreateProtection(options, timeProvider);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? first
            )
        );

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? second
            )
        );

        second.Dispose();

        Assert.False(protection.TryBeginAuthentication(s_address, out IAccountAuthenticationRequestLease? _));

        timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? replenished
            )
        );

        replenished.Dispose();
    }

    [Fact]
    public void TryBeginRequest_Ipv4MappedIpv6SharesIpv4SourceLimit()
    {
        AccountAuthenticationProtectionOptions options = new(requestLimitPerSource: 1);
        AccountAuthenticationProtection protection = CreateProtection(options);

        IPAddress ipv4 = IPAddress.Parse("192.0.2.10");
        IPAddress mapped = IPAddress.Parse("::ffff:192.0.2.10");

        Assert.True(
            protection.TryBeginAuthentication(
                ipv4,
                out IAccountAuthenticationRequestLease? first
            )
        );

        first.Dispose();

        Assert.False(
            protection.TryBeginAuthentication(
                mapped,
                out IAccountAuthenticationRequestLease? second
            )
        );

        Assert.Null(second);
    }

    [Fact]
    public void TryBeginRequest_Ipv6AddressesInSame64ShareSourceLimit()
    {
        AccountAuthenticationProtectionOptions options = new(requestLimitPerSource: 1);
        AccountAuthenticationProtection protection = CreateProtection(options);

        IPAddress firstAddress = IPAddress.Parse("2001:db8:1234:5678::1");
        IPAddress secondAddress = IPAddress.Parse("2001:db8:1234:5678::abcd");

        Assert.True(
            protection.TryBeginAuthentication(
                firstAddress,
                out IAccountAuthenticationRequestLease? first
            )
        );

        first.Dispose();

        Assert.False(
            protection.TryBeginAuthentication(
                secondAddress,
                out IAccountAuthenticationRequestLease? second
            )
        );

        Assert.Null(second);
    }

    [Fact]
    public void TryBeginRequest_DifferentIpv6PrefixesHaveIndependentSourceLimits()
    {
        AccountAuthenticationProtectionOptions options = new(requestLimitPerSource: 1);
        AccountAuthenticationProtection protection = CreateProtection(options);

        IPAddress firstAddress = IPAddress.Parse("2001:db8:1234:5678::1");
        IPAddress secondAddress = IPAddress.Parse("2001:db8:1234:5679::1");

        Assert.True(
            protection.TryBeginAuthentication(
                firstAddress,
                out IAccountAuthenticationRequestLease? first
            )
        );

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                secondAddress,
                out IAccountAuthenticationRequestLease? second
            )
        );

        second.Dispose();
    }

    [Fact]
    public void TryBeginRequest_TrackingCapacityFailsClosedUntilEntriesExpire()
    {
        ManualTimeProvider timeProvider = new();

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 1,
            requestWindow: TimeSpan.FromSeconds(1),
            failureWindow: TimeSpan.FromSeconds(1),
            failureLockout: TimeSpan.FromSeconds(1),
            entryRetention: TimeSpan.FromSeconds(1),
            maximumConcurrentRequests: 4,
            maximumTrackedEntries: 3
        );

        AccountAuthenticationProtection protection = CreateProtection(options, timeProvider);

        for (int i = 10; i <= 12; i++)
        {
            Assert.True(
                protection.TryBeginAuthentication(
                    IPAddress.Parse($"192.0.2.{i}"),
                    out IAccountAuthenticationRequestLease? request
                )
            );

            request.Dispose();
        }

        Assert.False(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.13"),
                out IAccountAuthenticationRequestLease? denied
            )
        );

        Assert.Null(denied);

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.13"),
                out IAccountAuthenticationRequestLease? admitted
            )
        );

        admitted.Dispose();
    }

    [Fact]
    public void RequestLease_MultipleDisposeCallsAreSafe()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                out IAccountAuthenticationRequestLease? request
            )
        );

        request.Dispose();
        request.Dispose();
    }

    [Fact]
    public void TryBeginAttempt_NullRemoteAddressThrows()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.Throws<ArgumentNullException>(() =>
            protection.TryBeginAuthentication(
                null!,
                1,
                out IAccountAuthenticationAttemptLease? _
            )
        );
    }

    [Fact]
    public void TryBeginAttempt_ZeroAccountIdThrows()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            protection.TryBeginAuthentication(
                s_address,
                0,
                out IAccountAuthenticationAttemptLease? _
            )
        );
    }

    [Fact]
    public void TryBeginAttempt_FirstAttemptIsAdmitted()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? attempt
            )
        );

        Assert.NotNull(attempt);

        attempt.Dispose();
    }

    [Fact]
    public void TryBeginAttempt_PerAccountConcurrencyLimitIsEnforcedAcrossSources()
    {
        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentAttemptsPerAccount: 1
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.10"),
                1,
                out IAccountAuthenticationAttemptLease? first
            )
        );

        Assert.False(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IAccountAuthenticationAttemptLease? second
            )
        );

        Assert.Null(second);

        first.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IAccountAuthenticationAttemptLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public void Complete_FailedAttemptsLockOnlyMatchingAccountAndSource()
    {
        AccountAuthenticationProtectionOptions options = new(
            failedAttemptLimitPerAccountSource: 2
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        CompleteFailed(protection, s_address, 1);
        CompleteFailed(protection, s_address, 1);

        Assert.False(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? locked
            )
        );

        Assert.Null(locked);

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IAccountAuthenticationAttemptLease? differentSource
            )
        );

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                2,
                out IAccountAuthenticationAttemptLease? differentAccount
            )
        );

        differentSource.Dispose();
        differentAccount.Dispose();
    }

    [Fact]
    public void TryBeginAttempt_LockoutExpires()
    {
        ManualTimeProvider timeProvider = new();

        AccountAuthenticationProtectionOptions options = new(
            failedAttemptLimitPerAccountSource: 2,
            failureWindow: TimeSpan.FromMinutes(5),
            failureLockout: TimeSpan.FromMinutes(5)
        );

        AccountAuthenticationProtection protection = CreateProtection(options, timeProvider);

        CompleteFailed(protection, s_address, 1);
        CompleteFailed(protection, s_address, 1);

        Assert.False(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? _
            )
        );

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? admitted
            )
        );

        admitted.Dispose();
    }

    [Fact]
    public void Complete_FailureWindowExpiresBeforeLaterFailures()
    {
        ManualTimeProvider timeProvider = new();

        AccountAuthenticationProtectionOptions options = new(
            failedAttemptLimitPerAccountSource: 2,
            failureWindow: TimeSpan.FromMinutes(2),
            failureLockout: TimeSpan.FromMinutes(5)
        );

        AccountAuthenticationProtection protection = CreateProtection(options, timeProvider);

        CompleteFailed(protection, s_address, 1);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        CompleteFailed(protection, s_address, 1);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? admitted
            )
        );

        admitted.Dispose();
    }

    [Fact]
    public void Dispose_AbandonedAttemptDoesNotCountAsCredentialFailure()
    {
        AccountAuthenticationProtectionOptions options = new(
            failedAttemptLimitPerAccountSource: 1
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? abandoned
            )
        );

        abandoned.Dispose();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? next
            )
        );

        next.Dispose();
    }

    [Fact]
    public void Complete_AcceptedCredentialsClearFailureState()
    {
        AccountAuthenticationProtectionOptions options = new(
            failedAttemptLimitPerAccountSource: 3
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        CompleteFailed(protection, s_address, 1);
        CompleteFailed(protection, s_address, 1);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? accepted
            )
        );

        accepted.Complete(credentialsAccepted: true);
        accepted.Dispose();

        CompleteFailed(protection, s_address, 1);
        CompleteFailed(protection, s_address, 1);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? fresh
            )
        );

        fresh.Dispose();
    }

    [Fact]
    public void TryBeginAttempt_InFlightAttemptsReserveFailureBudget()
    {
        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentAttemptsPerAccount: 4,
            failedAttemptLimitPerAccountSource: 2
        );

        AccountAuthenticationProtection protection = CreateProtection(options);

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? first
            )
        );

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? second
            )
        );

        Assert.False(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? third
            )
        );

        Assert.Null(third);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Complete_CannotBeCalledTwice()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? attempt
            )
        );

        attempt.Complete(credentialsAccepted: false);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(credentialsAccepted: false)
        );

        attempt.Dispose();
    }

    [Fact]
    public void Complete_AfterDisposeThrows()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? attempt
            )
        );

        attempt.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            attempt.Complete(credentialsAccepted: false)
        );
    }

    [Fact]
    public void AttemptLease_MultipleDisposeCallsAreSafe()
    {
        AccountAuthenticationProtection protection = CreateProtection();

        Assert.True(
            protection.TryBeginAuthentication(
                s_address,
                1,
                out IAccountAuthenticationAttemptLease? attempt
            )
        );

        attempt.Dispose();
        attempt.Dispose();
    }

    private static AccountAuthenticationProtection CreateProtection(
        AccountAuthenticationProtectionOptions? options = null,
        TimeProvider? timeProvider = null
    )
    {
        return new AccountAuthenticationProtection(
            options ?? new AccountAuthenticationProtectionOptions(),
            timeProvider ?? new ManualTimeProvider()
        );
    }

    private static void CompleteFailed(
        AccountAuthenticationProtection protection,
        IPAddress remoteAddress,
        uint accountId
    )
    {
        Assert.True(
            protection.TryBeginAuthentication(
                remoteAddress,
                accountId,
                out IAccountAuthenticationAttemptLease? attempt
            )
        );

        attempt.Complete(credentialsAccepted: false);
        attempt.Dispose();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Volatile.Read(ref _timestamp);
        }

        public void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref _timestamp, duration.Ticks);
        }
    }
}
