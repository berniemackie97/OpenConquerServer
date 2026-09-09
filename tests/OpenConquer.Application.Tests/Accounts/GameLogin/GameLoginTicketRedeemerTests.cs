using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.Application.Accounts.GameLogin;

namespace OpenConquer.Application.Tests.Accounts.GameLogin;

public sealed class GameLoginTicketRedeemerTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.20");

    [Fact]
    public void Constructor_RejectsNullRedemptionStore()
    {
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketRedeemer(null!, new FakeAttemptLimiter()));
    }

    [Fact]
    public void Constructor_RejectsNullAttemptLimiter()
    {
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketRedeemer(new FakeRedemptionStore(), null!));
    }

    [Fact]
    public async Task RedeemAsync_RejectsNullRemoteAddress()
    {
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAsync<ArgumentNullException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, limiter.BeginCount);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_PropagatesCallerCancellationBeforeAdmission()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, cancellation.Token).AsTask());

        Assert.Equal(0, limiter.BeginCount);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_ZeroSessionUidIsRejectedWithoutAdmissionOrPersistence()
    {
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(0, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Null(identity);
        Assert.Equal(0, limiter.BeginCount);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_ZeroAuthenticationKeyIsRejectedWithoutAdmissionOrPersistence()
    {
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(SessionUid, 0, s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Null(identity);
        Assert.Equal(0, limiter.BeginCount);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_ProtectionRejectionReturnsNullWithoutPersistence()
    {
        FakeRedemptionStore store = new();
        FakeAttemptLimiter limiter = new() { Admit = false };
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Null(identity);
        Assert.Equal(1, limiter.BeginCount);
        Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
        Assert.Equal(SessionUid, limiter.LastSessionUid);
        Assert.Null(limiter.LastAttempt);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_CancellationDuringProtectionRejectionIsObserved()
    {
        using CancellationTokenSource cancellation = new();

        FakeAttemptLimiter limiter = new() { Admit = false, OnBegin = cancellation.Cancel };
        FakeRedemptionStore store = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, cancellation.Token).AsTask());

        Assert.Equal(1, limiter.BeginCount);
        Assert.Equal(0, store.RedemptionCount);
    }

    [Fact]
    public async Task RedeemAsync_SuccessForwardsCredentialsAndCompletesAttemptAsAccepted()
    {
        GameLoginTicketIdentity expected = new(AccountId, Username, SessionUid);
        FakeRedemptionStore store = new() { Result = expected };
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Same(expected, identity);
        Assert.Equal(1, limiter.BeginCount);
        Assert.Equal(s_remoteAddress, limiter.LastRemoteAddress);
        Assert.Equal(SessionUid, limiter.LastSessionUid);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.True(attempt.AuthorizationAccepted);
        Assert.True(attempt.IsDisposed);

        Assert.Equal(1, store.RedemptionCount);
        Assert.Equal(SessionUid, store.LastSessionUid);
        Assert.Equal(AuthenticationKey, store.LastAuthenticationKey);
    }

    [Fact]
    public async Task RedeemAsync_UnredeemableTicketCompletesAttemptAsRejected()
    {
        FakeRedemptionStore store = new() { Result = null };
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken);

        Assert.Null(identity);
        Assert.Equal(1, store.RedemptionCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.False(attempt.AuthorizationAccepted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task RedeemAsync_PersistenceFailureAbandonsAttemptWithoutRetry()
    {
        FakeRedemptionStore store = new() { Exception = new IOException("Persistence failed.") };
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAsync<IOException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.RedemptionCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.False(attempt.IsCompleted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task RedeemAsync_CancellationDuringPersistenceAbandonsAttempt()
    {
        using CancellationTokenSource cancellation = new();

        FakeRedemptionStore store = new()
        {
            OnRedeem = cancellation.Cancel,
            ObserveCancellationAfterCallback = true,
        };

        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, cancellation.Token).AsTask());

        Assert.Equal(1, store.RedemptionCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.False(attempt.IsCompleted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task RedeemAsync_MismatchedSessionIdentityFailsClosedAndAbandonsAttempt()
    {
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid + 1) };
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.RedemptionCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.False(attempt.IsCompleted);
        Assert.True(attempt.IsDisposed);
    }

    [Fact]
    public async Task RedeemAsync_CancellationAfterSuccessfulConsumptionDoesNotHideSuccess()
    {
        using CancellationTokenSource cancellation = new();

        GameLoginTicketIdentity expected = new(AccountId, Username, SessionUid);
        FakeRedemptionStore store = new() { Result = expected, OnRedeem = cancellation.Cancel };
        FakeAttemptLimiter limiter = new();
        GameLoginTicketRedeemer redeemer = new(store, limiter);

        GameLoginTicketIdentity? identity = await redeemer.RedeemAsync(SessionUid, AuthenticationKey, s_remoteAddress, cancellation.Token);

        Assert.Same(expected, identity);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.RedemptionCount);

        FakeAttemptLease attempt = Assert.IsType<FakeAttemptLease>(limiter.LastAttempt);

        Assert.True(attempt.IsCompleted);
        Assert.True(attempt.AuthorizationAccepted);
        Assert.True(attempt.IsDisposed);
    }

    private sealed class FakeAttemptLimiter : IGameLoginTicketRedemptionAttemptLimiter
    {
        public bool Admit { get; init; } = true;
        public Action? OnBegin { get; init; }
        public int BeginCount { get; private set; }
        public IPAddress? LastRemoteAddress { get; private set; }
        public uint LastSessionUid { get; private set; }
        public FakeAttemptLease? LastAttempt { get; private set; }

        public bool TryBeginRedemption(IPAddress remoteAddress, uint sessionUid, [NotNullWhen(true)] out IGameLoginTicketRedemptionAttemptLease? attempt)
        {
            ArgumentNullException.ThrowIfNull(remoteAddress);

            BeginCount++;
            LastRemoteAddress = remoteAddress;
            LastSessionUid = sessionUid;

            OnBegin?.Invoke();

            if (!Admit)
            {
                attempt = null;
                return false;
            }

            FakeAttemptLease concreteAttempt = new();

            LastAttempt = concreteAttempt;
            attempt = concreteAttempt;

            return true;
        }
    }

    private sealed class FakeAttemptLease : IGameLoginTicketRedemptionAttemptLease
    {
        public bool IsCompleted { get; private set; }
        public bool AuthorizationAccepted { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Complete(bool authorizationAccepted)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FakeAttemptLease));
            }

            if (IsCompleted)
            {
                throw new InvalidOperationException("The redemption attempt has already been completed.");
            }

            IsCompleted = true;
            AuthorizationAccepted = authorizationAccepted;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeRedemptionStore : IGameLoginTicketRedemptionStore
    {
        public GameLoginTicketIdentity? Result { get; init; }
        public Exception? Exception { get; init; }
        public Action? OnRedeem { get; init; }
        public bool ObserveCancellationAfterCallback { get; init; }
        public int RedemptionCount { get; private set; }
        public uint? LastSessionUid { get; private set; }
        public uint? LastAuthenticationKey { get; private set; }

        public ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RedemptionCount++;
            LastSessionUid = sessionUid;
            LastAuthenticationKey = authenticationKey;

            OnRedeem?.Invoke();

            if (ObserveCancellationAfterCallback)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (Exception is not null)
            {
                throw Exception;
            }

            return ValueTask.FromResult(Result);
        }
    }
}
