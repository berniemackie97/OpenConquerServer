using System.Collections.Concurrent;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketRedemptionAttemptLimiterConcurrencyTests
{
    [Fact]
    public async Task TryBeginRedemption_ConcurrentSameSessionAdmitsExactlyOne()
    {
        const int attemptCount = 64;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: attemptCount,
            maximumConcurrentAttemptsPerSession: 1,
            maximumConcurrentAttempts: attemptCount
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);
        ConcurrentBag<IGameLoginTicketRedemptionAttemptLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] attempts = new Task<bool>[attemptCount];

        for (int index = 0; index < attemptCount; index++)
        {
            int capturedIndex = index;

            attempts[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    IPAddress address = CreateAddress(capturedIndex);
                    bool admitted = limiter.TryBeginRedemption(
                        address,
                        1,
                        out IGameLoginTicketRedemptionAttemptLease? lease
                    );

                    if (admitted)
                    {
                        leases.Add(lease!);
                    }

                    return admitted;
                },
                TestContext.Current.CancellationToken
            );
        }

        start.Set();

        bool[] results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(static admitted => admitted));
        Assert.Single(leases);

        foreach (IGameLoginTicketRedemptionAttemptLease lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task TryBeginRedemption_ConcurrentAttemptsRespectGlobalConcurrencyLimit()
    {
        const int attemptCount = 64;
        const int concurrencyLimit = 8;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: concurrencyLimit,
            maximumConcurrentAttemptsPerSession: 1,
            maximumConcurrentAttempts: concurrencyLimit
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);
        ConcurrentBag<IGameLoginTicketRedemptionAttemptLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] attempts = new Task<bool>[attemptCount];

        for (int index = 0; index < attemptCount; index++)
        {
            int capturedIndex = index;

            attempts[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = limiter.TryBeginRedemption(
                        CreateAddress(capturedIndex),
                        (uint)(capturedIndex + 1),
                        out IGameLoginTicketRedemptionAttemptLease? lease
                    );

                    if (admitted)
                    {
                        leases.Add(lease!);
                    }

                    return admitted;
                },
                TestContext.Current.CancellationToken
            );
        }

        start.Set();

        bool[] results = await Task.WhenAll(attempts);

        Assert.Equal(concurrencyLimit, results.Count(static admitted => admitted));
        Assert.Equal(concurrencyLimit, leases.Count);

        foreach (IGameLoginTicketRedemptionAttemptLease lease in leases)
        {
            lease.Dispose();
        }

        Assert.True(
            limiter.TryBeginRedemption(
                CreateAddress(attemptCount + 1),
                10_000,
                out IGameLoginTicketRedemptionAttemptLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public async Task TryBeginRedemption_DistributedConcurrentGuessingCannotExceedSessionFailureBudget()
    {
        const int attemptCount = 64;
        const int failureLimit = 8;
        const uint sessionUid = 1;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: failureLimit,
            maximumConcurrentAttemptsPerSession: failureLimit,
            failedAttemptLimitPerSession: failureLimit,
            maximumConcurrentAttempts: attemptCount
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);
        ConcurrentBag<IGameLoginTicketRedemptionAttemptLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] attempts = new Task<bool>[attemptCount];

        for (int index = 0; index < attemptCount; index++)
        {
            int capturedIndex = index;

            attempts[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = limiter.TryBeginRedemption(
                        CreateAddress(capturedIndex),
                        sessionUid,
                        out IGameLoginTicketRedemptionAttemptLease? lease
                    );

                    if (admitted)
                    {
                        leases.Add(lease!);
                    }

                    return admitted;
                },
                TestContext.Current.CancellationToken
            );
        }

        start.Set();

        bool[] results = await Task.WhenAll(attempts);

        Assert.Equal(failureLimit, results.Count(static admitted => admitted));
        Assert.Equal(failureLimit, leases.Count);

        foreach (IGameLoginTicketRedemptionAttemptLease lease in leases)
        {
            lease.Complete(authorizationAccepted: false);
            lease.Dispose();
        }

        Assert.False(
            limiter.TryBeginRedemption(
                CreateAddress(attemptCount + 1),
                sessionUid,
                out IGameLoginTicketRedemptionAttemptLease? lockedOut
            )
        );

        Assert.Null(lockedOut);
    }

    [Fact]
    public async Task TryBeginRedemption_CapacityPressureDoesNotEvictActiveSecurityState()
    {
        const int pressureAttemptCount = 64;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: 4,
            maximumConcurrentAttemptsPerSession: 1,
            maximumConcurrentAttempts: pressureAttemptCount,
            maximumTrackedEntries: 4
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);

        Assert.True(
            limiter.TryBeginRedemption(
                CreateAddress(0),
                1,
                out IGameLoginTicketRedemptionAttemptLease? first
            )
        );
        Assert.True(
            limiter.TryBeginRedemption(
                CreateAddress(1),
                2,
                out IGameLoginTicketRedemptionAttemptLease? second
            )
        );

        Task<bool>[] pressureAttempts = new Task<bool>[pressureAttemptCount];

        for (int index = 0; index < pressureAttemptCount; index++)
        {
            int capturedIndex = index;

            pressureAttempts[index] = Task.Run(
                () =>
                    limiter.TryBeginRedemption(
                        CreateAddress(capturedIndex + 100),
                        (uint)(capturedIndex + 100),
                        out _
                    ),
                TestContext.Current.CancellationToken
            );
        }

        bool[] results = await Task.WhenAll(pressureAttempts);

        Assert.All(results, static admitted => Assert.False(admitted));

        Assert.False(limiter.TryBeginRedemption(CreateAddress(500), 1, out _));
        Assert.False(limiter.TryBeginRedemption(CreateAddress(501), 2, out _));

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public async Task AttemptLease_ConcurrentCompleteAndDisposeReleaseReservationExactlyOnce()
    {
        const int iterationCount = 256;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            requestLimitPerSource: 1,
            failedAttemptLimitPerSession: 2,
            maximumConcurrentAttempts: iterationCount
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);

        for (int index = 0; index < iterationCount; index++)
        {
            uint sessionUid = (uint)(index + 1);

            Assert.True(
                limiter.TryBeginRedemption(
                    CreateAddress(index),
                    sessionUid,
                    out IGameLoginTicketRedemptionAttemptLease? lease
                )
            );

            using ManualResetEventSlim start = new(false);

            Task<Exception?> complete = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    try
                    {
                        lease.Complete(authorizationAccepted: false);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                },
                TestContext.Current.CancellationToken
            );

            Task dispose = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    lease.Dispose();
                },
                TestContext.Current.CancellationToken
            );

            start.Set();

            await Task.WhenAll(complete, dispose);

            Exception? completeException = await complete;

            Assert.True(completeException is null or ObjectDisposedException);

            Assert.True(
                limiter.TryBeginRedemption(
                    CreateAddress(index + iterationCount),
                    sessionUid,
                    out IGameLoginTicketRedemptionAttemptLease? verification
                )
            );

            verification.Dispose();
        }
    }

    [Fact]
    public async Task AttemptLease_ManyIndependentConcurrentCompletionsDoNotLeakGlobalCapacity()
    {
        const int attemptCount = 128;

        GameLoginTicketRedemptionAttemptLimiterOptions options = new(
            maximumConcurrentAttemptsPerSource: attemptCount,
            maximumConcurrentAttemptsPerSession: 1,
            maximumConcurrentAttempts: attemptCount
        );

        GameLoginTicketRedemptionAttemptLimiter limiter = new(options, TimeProvider.System);
        ConcurrentBag<IGameLoginTicketRedemptionAttemptLease> leases = [];

        for (int index = 0; index < attemptCount; index++)
        {
            Assert.True(
                limiter.TryBeginRedemption(
                    CreateAddress(index),
                    (uint)(index + 1),
                    out IGameLoginTicketRedemptionAttemptLease? lease
                )
            );

            leases.Add(lease);
        }

        Task[] completions = leases
            .Select(
                (lease, index) =>
                    Task.Run(
                        () =>
                        {
                            if ((index & 1) == 0)
                            {
                                lease.Complete(authorizationAccepted: false);
                            }

                            lease.Dispose();
                        },
                        TestContext.Current.CancellationToken
                    )
            )
            .ToArray();

        await Task.WhenAll(completions);

        Assert.True(
            limiter.TryBeginRedemption(
                CreateAddress(attemptCount + 1),
                10_000,
                out IGameLoginTicketRedemptionAttemptLease? verification
            )
        );

        verification.Dispose();
    }

    private static IPAddress CreateAddress(int index)
    {
        int thirdOctet = index / 254;
        int fourthOctet = index % 254 + 1;

        return IPAddress.Parse($"198.18.{thirdOctet}.{fourthOctet}");
    }
}
