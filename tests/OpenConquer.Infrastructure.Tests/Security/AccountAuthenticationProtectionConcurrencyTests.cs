using System.Collections.Concurrent;
using System.Net;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class AccountAuthenticationProtectionConcurrencyTests
{
    [Fact]
    public async Task TryBeginRequest_ConcurrentSameSourceRespectsPerSourceConcurrencyLimit()
    {
        const int requestCount = 64;
        const int concurrencyLimit = 4;

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: requestCount,
            maximumConcurrentRequestsPerSource: concurrencyLimit,
            maximumConcurrentRequests: requestCount
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);
        ConcurrentBag<IAccountAuthenticationRequestLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] requests = new Task<bool>[requestCount];

        for (int index = 0; index < requestCount; index++)
        {
            requests[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = protection.TryBeginAuthentication(
                        IPAddress.Parse("192.0.2.10"),
                        out IAccountAuthenticationRequestLease? lease
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

        bool[] results = await Task.WhenAll(requests);

        Assert.Equal(concurrencyLimit, results.Count(static admitted => admitted));
        Assert.Equal(concurrencyLimit, leases.Count);

        foreach (IAccountAuthenticationRequestLease lease in leases)
        {
            lease.Dispose();
        }

        Assert.True(
            protection.TryBeginAuthentication(
                IPAddress.Parse("192.0.2.10"),
                out IAccountAuthenticationRequestLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public async Task TryBeginRequest_ConcurrentRequestsRespectGlobalConcurrencyLimit()
    {
        const int requestCount = 64;
        const int concurrencyLimit = 8;

        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequestsPerSource: concurrencyLimit,
            maximumConcurrentRequests: concurrencyLimit
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);
        ConcurrentBag<IAccountAuthenticationRequestLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] requests = new Task<bool>[requestCount];

        for (int index = 0; index < requestCount; index++)
        {
            int capturedIndex = index;

            requests[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = protection.TryBeginAuthentication(
                        CreateAddress(capturedIndex),
                        out IAccountAuthenticationRequestLease? lease
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

        bool[] results = await Task.WhenAll(requests);

        Assert.Equal(concurrencyLimit, results.Count(static admitted => admitted));
        Assert.Equal(concurrencyLimit, leases.Count);

        foreach (IAccountAuthenticationRequestLease lease in leases)
        {
            lease.Dispose();
        }

        Assert.True(
            protection.TryBeginAuthentication(
                CreateAddress(requestCount + 1),
                out IAccountAuthenticationRequestLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public async Task TryBeginAttempt_ConcurrentSameAccountRespectsPerAccountConcurrencyLimit()
    {
        const int attemptCount = 64;
        const int concurrencyLimit = 4;
        const uint accountId = 42;

        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequests: attemptCount,
            maximumConcurrentAttemptsPerAccount: concurrencyLimit
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);
        ConcurrentBag<IAccountAuthenticationAttemptLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] attempts = new Task<bool>[attemptCount];

        for (int index = 0; index < attemptCount; index++)
        {
            int capturedIndex = index;

            attempts[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = protection.TryBeginAuthentication(
                        CreateAddress(capturedIndex),
                        accountId,
                        out IAccountAuthenticationAttemptLease? lease
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

        foreach (IAccountAuthenticationAttemptLease lease in leases)
        {
            lease.Dispose();
        }

        Assert.True(
            protection.TryBeginAuthentication(
                CreateAddress(attemptCount + 1),
                accountId,
                out IAccountAuthenticationAttemptLease? afterRelease
            )
        );

        afterRelease.Dispose();
    }

    [Fact]
    public async Task TryBeginAttempt_ConcurrentGuessingCannotExceedAccountSourceFailureBudget()
    {
        const int attemptCount = 64;
        const int failureLimit = 8;
        const uint accountId = 42;

        IPAddress address = IPAddress.Parse("192.0.2.10");

        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequests: attemptCount,
            maximumConcurrentAttemptsPerAccount: failureLimit,
            failedAttemptLimitPerAccountSource: failureLimit
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);
        ConcurrentBag<IAccountAuthenticationAttemptLease> leases = [];
        using ManualResetEventSlim start = new(false);

        Task<bool>[] attempts = new Task<bool>[attemptCount];

        for (int index = 0; index < attemptCount; index++)
        {
            attempts[index] = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    bool admitted = protection.TryBeginAuthentication(
                        address,
                        accountId,
                        out IAccountAuthenticationAttemptLease? lease
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

        foreach (IAccountAuthenticationAttemptLease lease in leases)
        {
            lease.Complete(credentialsAccepted: false);
            lease.Dispose();
        }

        Assert.False(
            protection.TryBeginAuthentication(
                address,
                accountId,
                out IAccountAuthenticationAttemptLease? lockedOut
            )
        );

        Assert.Null(lockedOut);
    }

    [Fact]
    public async Task CapacityPressureDoesNotEvictActiveAuthenticationState()
    {
        const int pressureRequestCount = 64;
        const uint accountId = 42;

        IPAddress activeAddress = IPAddress.Parse("192.0.2.10");

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: pressureRequestCount,
            maximumConcurrentRequestsPerSource: 1,
            maximumConcurrentRequests: pressureRequestCount,
            maximumConcurrentAttemptsPerAccount: 1,
            maximumTrackedEntries: 3
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);

        Assert.True(
            protection.TryBeginAuthentication(
                activeAddress,
                out IAccountAuthenticationRequestLease? activeRequest
            )
        );

        Assert.True(
            protection.TryBeginAuthentication(
                activeAddress,
                accountId,
                out IAccountAuthenticationAttemptLease? activeAttempt
            )
        );

        Task<bool>[] pressureRequests = new Task<bool>[pressureRequestCount];

        for (int index = 0; index < pressureRequestCount; index++)
        {
            int capturedIndex = index;

            pressureRequests[index] = Task.Run(
                () =>
                    protection.TryBeginAuthentication(
                        CreateAddress(capturedIndex + 100),
                        out IAccountAuthenticationRequestLease? _
                    ),
                TestContext.Current.CancellationToken
            );
        }

        bool[] results = await Task.WhenAll(pressureRequests);

        Assert.All(results, static admitted => Assert.False(admitted));

        Assert.False(
            protection.TryBeginAuthentication(
                activeAddress,
                out IAccountAuthenticationRequestLease? duplicateRequest
            )
        );

        Assert.Null(duplicateRequest);

        Assert.False(
            protection.TryBeginAuthentication(
                CreateAddress(500),
                accountId,
                out IAccountAuthenticationAttemptLease? duplicateAttempt
            )
        );

        Assert.Null(duplicateAttempt);

        activeAttempt.Dispose();
        activeRequest.Dispose();
    }

    [Fact]
    public async Task AttemptLease_ConcurrentCompleteAndDisposeReleaseReservationExactlyOnce()
    {
        const int iterationCount = 256;

        AccountAuthenticationProtectionOptions options = new(
            maximumConcurrentRequests: iterationCount,
            maximumConcurrentAttemptsPerAccount: 1,
            failedAttemptLimitPerAccountSource: 2
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);

        for (int index = 0; index < iterationCount; index++)
        {
            uint accountId = (uint)(index + 1);

            Assert.True(
                protection.TryBeginAuthentication(
                    CreateAddress(index),
                    accountId,
                    out IAccountAuthenticationAttemptLease? lease
                )
            );

            using ManualResetEventSlim start = new(false);

            Task<Exception?> complete = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);

                    try
                    {
                        lease.Complete(credentialsAccepted: false);
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
                protection.TryBeginAuthentication(
                    CreateAddress(index + iterationCount),
                    accountId,
                    out IAccountAuthenticationAttemptLease? verification
                )
            );

            verification.Dispose();
        }
    }

    [Fact]
    public async Task RequestLease_ConcurrentDisposeReleasesReservationExactlyOnce()
    {
        const int iterationCount = 256;

        IPAddress address = IPAddress.Parse("192.0.2.10");

        AccountAuthenticationProtectionOptions options = new(
            requestLimitPerSource: 1_000_000,
            maximumConcurrentRequestsPerSource: 1,
            maximumConcurrentRequests: 1,
            maximumConcurrentAttemptsPerAccount: 1
        );

        AccountAuthenticationProtection protection = new(options, TimeProvider.System);

        for (int index = 0; index < iterationCount; index++)
        {
            Assert.True(
                protection.TryBeginAuthentication(
                    address,
                    out IAccountAuthenticationRequestLease? lease
                )
            );

            using ManualResetEventSlim start = new(false);

            Task firstDispose = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    lease.Dispose();
                },
                TestContext.Current.CancellationToken
            );

            Task secondDispose = Task.Run(
                () =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    lease.Dispose();
                },
                TestContext.Current.CancellationToken
            );

            start.Set();

            await Task.WhenAll(firstDispose, secondDispose);

            Assert.True(
                protection.TryBeginAuthentication(
                    address,
                    out IAccountAuthenticationRequestLease? verification
                )
            );

            verification.Dispose();
        }
    }

    private static IPAddress CreateAddress(int index)
    {
        int thirdOctet = index / 254;
        int fourthOctet = index % 254 + 1;

        return IPAddress.Parse($"198.18.{thirdOctet}.{fourthOctet}");
    }
}
