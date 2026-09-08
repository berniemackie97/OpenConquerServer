using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketRedemptionAttemptLimiterTests
{
    private static readonly IPAddress s_address = IPAddress.Parse("192.0.2.10");

    [Fact]
    public void Constructor_NullOptionsThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketRedemptionAttemptLimiter(null!, TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_NullTimeProviderThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketRedemptionAttemptLimiter(
                new GameLoginTicketRedemptionAttemptLimiterOptions(),
                null!
            )
        );
    }

    [Fact]
    public void TryBeginRedemption_NullRemoteAddressThrows()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        Assert.Throws<ArgumentNullException>(() => limiter.TryBeginRedemption(null!, 1, out _));
    }

    [Fact]
    public void TryBeginRedemption_ZeroSessionUidThrows()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            limiter.TryBeginRedemption(s_address, 0, out _)
        );
    }

    [Fact]
    public void TryBeginRedemption_FirstAttemptIsAdmitted()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        bool admitted = limiter.TryBeginRedemption(
            s_address,
            1,
            out IGameLoginTicketRedemptionAttemptLease? attempt
        );

        Assert.True(admitted);
        Assert.NotNull(attempt);

        attempt.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_PerSessionConcurrencyLimitIsEnforced()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSession: 1
        );
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        Assert.False(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.Null(second);

        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? afterRelease
            )
        );
        afterRelease.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_PerSourceConcurrencyLimitIsEnforcedAcrossSessions()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: 1
        );
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        Assert.False(
            limiter.TryBeginRedemption(
                s_address,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.Null(second);

        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                2,
                out IGameLoginTicketRedemptionAttemptLease? afterRelease
            )
        );
        afterRelease.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_GlobalConcurrencyLimitIsEnforced()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: 2,
            maximumConcurrentAttemptsPerSession: 2,
            maximumConcurrentAttempts: 2
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.10"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.False(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                3,
                out IGameLoginTicketRedemptionAttemptLease? third
            )
        );
        Assert.Null(third);

        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                3,
                out IGameLoginTicketRedemptionAttemptLease? afterRelease
            )
        );

        second.Dispose();
        afterRelease.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_SourceRequestLimitIsConsumedByAbandonedAttempts()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            requestLimitPerSource: 2,
            requestWindow: TimeSpan.FromMinutes(1)
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        second.Dispose();

        Assert.False(
            limiter.TryBeginRedemption(
                s_address,
                3,
                out IGameLoginTicketRedemptionAttemptLease? denied
            )
        );
        Assert.Null(denied);
    }

    [Fact]
    public void TryBeginRedemption_SourceRequestLimitRefillsOverTime()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            requestLimitPerSource: 2,
            requestWindow: TimeSpan.FromMinutes(1)
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        second.Dispose();

        Assert.False(limiter.TryBeginRedemption(s_address, 3, out _));

        timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                3,
                out IGameLoginTicketRedemptionAttemptLease? replenished
            )
        );
        replenished.Dispose();
    }

    [Fact]
    public void Complete_RejectedAttemptsLockSessionAtFailureLimit()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            failedAttemptLimitPerSession: 3,
            failureWindow: TimeSpan.FromMinutes(5),
            failureLockout: TimeSpan.FromMinutes(5)
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.10"), 1);
        CompleteRejected(limiter, IPAddress.Parse("192.0.2.11"), 1);
        CompleteRejected(limiter, IPAddress.Parse("192.0.2.12"), 1);

        Assert.False(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.13"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? denied
            )
        );
        Assert.Null(denied);
    }

    [Fact]
    public void TryBeginRedemption_LockoutExpires()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            failedAttemptLimitPerSession: 2,
            failureWindow: TimeSpan.FromMinutes(5),
            failureLockout: TimeSpan.FromMinutes(5)
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.10"), 1);
        CompleteRejected(limiter, IPAddress.Parse("192.0.2.11"), 1);

        Assert.False(limiter.TryBeginRedemption(IPAddress.Parse("192.0.2.12"), 1, out _));

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? admitted
            )
        );
        admitted.Dispose();
    }

    [Fact]
    public void Complete_FailureWindowExpiresBeforeLaterFailures()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            failedAttemptLimitPerSession: 2,
            failureWindow: TimeSpan.FromMinutes(2),
            failureLockout: TimeSpan.FromMinutes(5)
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.10"), 1);

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.11"), 1);

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? admitted
            )
        );
        admitted.Dispose();
    }

    [Fact]
    public void Dispose_AbandonedAttemptDoesNotCountAsAuthorizationFailure()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            failedAttemptLimitPerSession: 1
        );
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? abandoned
            )
        );
        abandoned.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? next
            )
        );
        next.Dispose();
    }

    [Fact]
    public void Complete_AcceptedAuthorizationClearsSessionFailureState()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            failedAttemptLimitPerSession: 3
        );
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.10"), 1);
        CompleteRejected(limiter, IPAddress.Parse("192.0.2.11"), 1);

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? accepted
            )
        );
        accepted.Complete(authorizationAccepted: true);
        accepted.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.13"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? fresh
            )
        );
        fresh.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_InFlightAttemptsReserveFailureBudget()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSession: 4,
            failedAttemptLimitPerSession: 2
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.10"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.False(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.12"),
                1,
                out IGameLoginTicketRedemptionAttemptLease? third
            )
        );
        Assert.Null(third);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_Ipv4MappedIpv6SharesIpv4SourceLimit()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(requestLimitPerSource: 1);
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        IPAddress ipv4 = IPAddress.Parse("192.0.2.10");
        IPAddress mapped = IPAddress.Parse("::ffff:192.0.2.10");

        Assert.True(
            limiter.TryBeginRedemption(ipv4, 1, out IGameLoginTicketRedemptionAttemptLease? first)
        );
        first.Dispose();

        Assert.False(
            limiter.TryBeginRedemption(
                mapped,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.Null(second);
    }

    [Fact]
    public void TryBeginRedemption_Ipv6AddressesInSame64ShareSourceLimit()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(requestLimitPerSource: 1);
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        IPAddress firstAddress = IPAddress.Parse("2001:db8:1234:5678::1");
        IPAddress secondAddress = IPAddress.Parse("2001:db8:1234:5678::abcd");

        Assert.True(
            limiter.TryBeginRedemption(
                firstAddress,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        first.Dispose();

        Assert.False(
            limiter.TryBeginRedemption(
                secondAddress,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        Assert.Null(second);
    }

    [Fact]
    public void TryBeginRedemption_DifferentIpv6PrefixesHaveIndependentSourceLimits()
    {
        GameLoginTicketRedemptionAttemptLimiterOptions options = new(requestLimitPerSource: 1);
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options);

        IPAddress firstAddress = IPAddress.Parse("2001:db8:1234:5678::1");
        IPAddress secondAddress = IPAddress.Parse("2001:db8:1234:5679::1");

        Assert.True(
            limiter.TryBeginRedemption(
                firstAddress,
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        first.Dispose();

        Assert.True(
            limiter.TryBeginRedemption(
                secondAddress,
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );
        second.Dispose();
    }

    [Fact]
    public void TryBeginRedemption_TrackingCapacityFailsClosedUntilEntriesExpire()
    {
        ManualTimeProvider timeProvider = new();

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            requestLimitPerSource: 1,
            requestWindow: TimeSpan.FromSeconds(1),
            failedAttemptLimitPerSession: 2,
            failureWindow: TimeSpan.FromSeconds(1),
            failureLockout: TimeSpan.FromSeconds(1),
            entryRetention: TimeSpan.FromSeconds(1),
            maximumTrackedEntries: 2
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter(options, timeProvider);

        CompleteRejected(limiter, IPAddress.Parse("192.0.2.10"), 1);

        Assert.False(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                2,
                out IGameLoginTicketRedemptionAttemptLease? denied
            )
        );
        Assert.Null(denied);

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        Assert.True(
            limiter.TryBeginRedemption(
                IPAddress.Parse("192.0.2.11"),
                2,
                out IGameLoginTicketRedemptionAttemptLease? admitted
            )
        );
        admitted.Dispose();
    }

    [Fact]
    public void Complete_CannotBeCalledTwice()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? attempt
            )
        );

        attempt.Complete(authorizationAccepted: false);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(authorizationAccepted: false)
        );

        attempt.Dispose();
    }

    [Fact]
    public void Complete_AfterDisposeThrows()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? attempt
            )
        );

        attempt.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            attempt.Complete(authorizationAccepted: false)
        );
    }

    [Fact]
    public void Dispose_MultipleCallsAreSafe()
    {
        GameLoginTicketRedemptionAttemptLimiter limiter = CreateLimiter();

        Assert.True(
            limiter.TryBeginRedemption(
                s_address,
                1,
                out IGameLoginTicketRedemptionAttemptLease? attempt
            )
        );

        attempt.Dispose();
        attempt.Dispose();
    }

    private static GameLoginTicketRedemptionAttemptLimiter CreateLimiter(
        GameLoginTicketRedemptionAttemptLimiterOptions? options = null,
        TimeProvider? timeProvider = null
    )
    {
        return new GameLoginTicketRedemptionAttemptLimiter(
            options ?? new GameLoginTicketRedemptionAttemptLimiterOptions(),
            timeProvider ?? new ManualTimeProvider()
        );
    }

    private static void CompleteRejected(
        GameLoginTicketRedemptionAttemptLimiter limiter,
        IPAddress remoteAddress,
        uint sessionUid
    )
    {
        Assert.True(
            limiter.TryBeginRedemption(
                remoteAddress,
                sessionUid,
                out IGameLoginTicketRedemptionAttemptLease? attempt
            )
        );

        attempt.Complete(authorizationAccepted: false);
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
