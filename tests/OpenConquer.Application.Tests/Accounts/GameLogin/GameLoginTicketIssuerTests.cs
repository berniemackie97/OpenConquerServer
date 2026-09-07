using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;

namespace OpenConquer.Application.Tests.Accounts.GameLogin;

public sealed class GameLoginTicketIssuerTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const ulong AccountStateRevision = 7;
    private const ulong PasswordCredentialRevision = 11;

    private static readonly DateTimeOffset s_issuedAtUtc = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_RejectsNullGrantStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketIssuer(null!, new SequenceTokenGenerator(), TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_RejectsNullTokenGenerator()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketIssuer(new FakeGrantStore(), null!, TimeProvider.System)
        );
    }

    [Fact]
    public void Constructor_RejectsNullTimeProvider()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketIssuer(new FakeGrantStore(), new SequenceTokenGenerator(), null!)
        );
    }

    [Fact]
    public async Task IssueAsync_RejectsUnsuccessfulAuthentication()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            issuer
                .IssueAsync(
                    AccountAuthenticationResult.InvalidCredentials(),
                    TestContext.Current.CancellationToken
                )
                .AsTask()
        );

        Assert.Equal(0, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_RejectsBannedAuthentication()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            issuer
                .IssueAsync(
                    AccountAuthenticationResult.Banned(),
                    TestContext.Current.CancellationToken
                )
                .AsTask()
        );

        Assert.Equal(0, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_PropagatesCallerCancellationBeforeAllocation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            issuer.IssueAsync(CreateSuccessfulAuthentication(), cancellation.Token).AsTask()
        );

        Assert.Equal(0, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_GrantedTicketUsesExpectedIdentityCredentialsAndLifetime()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new(
            sessionUids: [0x1020_3040u],
            authenticationKeys: [0x5060_7080u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            TestContext.Current.CancellationToken
        );

        GameLoginTicket granted = Assert.IsType<GameLoginTicket>(ticket);

        Assert.Equal(AccountId, granted.AccountId);
        Assert.Equal(Username, granted.Username);
        Assert.Equal(0x1020_3040u, granted.SessionUid);
        Assert.Equal(0x5060_7080u, granted.AuthenticationKey);
        Assert.Equal(s_issuedAtUtc, granted.IssuedAtUtc);
        Assert.Equal(s_issuedAtUtc.AddMinutes(5), granted.ExpiresAtUtc);

        Assert.Equal(1, store.GrantCount);
        Assert.Same(granted, store.GrantedTickets.Single());
        Assert.Equal(AccountStateRevision, store.AccountStateRevisions.Single());
        Assert.Equal(PasswordCredentialRevision, store.PasswordCredentialRevisions.Single());
    }

    [Fact]
    public async Task IssueAsync_NormalizesIssueTimeToUtc()
    {
        DateTimeOffset suppliedTime = new(2026, 9, 6, 18, 0, 0, TimeSpan.FromHours(-4));

        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(suppliedTime));

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            TestContext.Current.CancellationToken
        );

        GameLoginTicket granted = Assert.IsType<GameLoginTicket>(ticket);

        Assert.Equal(TimeSpan.Zero, granted.IssuedAtUtc.Offset);
        Assert.Equal(suppliedTime.ToUniversalTime(), granted.IssuedAtUtc);
        Assert.Equal(suppliedTime.ToUniversalTime().AddMinutes(5), granted.ExpiresAtUtc);
    }

    [Fact]
    public async Task IssueAsync_TimeOutsideRepresentableLifetimeFailsClosed()
    {
        GameLoginTicketIssuer issuer = new(
            new FakeGrantStore(),
            new SequenceTokenGenerator(),
            new FixedTimeProvider(DateTimeOffset.MaxValue)
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );
    }

    [Fact]
    public async Task IssueAsync_RejectsZeroSessionUidFromGenerator()
    {
        FakeGrantStore store = new();

        SequenceTokenGenerator tokens = new(sessionUids: [0u], authenticationKeys: [123u]);

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_RejectsZeroAuthenticationKeyFromGenerator()
    {
        FakeGrantStore store = new();

        SequenceTokenGenerator tokens = new(sessionUids: [123u], authenticationKeys: [0u]);

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_SessionUidCollisionGeneratesEntirelyNewCredentialPair()
    {
        FakeGrantStore store = new(
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.Granted
        );

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 202u],
            authenticationKeys: [303u, 404u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            TestContext.Current.CancellationToken
        );

        GameLoginTicket granted = Assert.IsType<GameLoginTicket>(ticket);

        Assert.Equal(2, store.GrantCount);
        Assert.Equal(2, tokens.SessionUidGenerationCount);
        Assert.Equal(2, tokens.AuthenticationKeyGenerationCount);

        GameLoginTicket first = store.GrantedTickets[0];
        GameLoginTicket second = store.GrantedTickets[1];

        Assert.Equal(101u, first.SessionUid);
        Assert.Equal(303u, first.AuthenticationKey);

        Assert.Equal(202u, second.SessionUid);
        Assert.Equal(404u, second.AuthenticationKey);

        Assert.Equal(first.IssuedAtUtc, second.IssuedAtUtc);
        Assert.Equal(first.ExpiresAtUtc, second.ExpiresAtUtc);

        Assert.Same(second, granted);
    }

    [Fact]
    public async Task IssueAsync_AuthenticationStateChangedReturnsNullWithoutRetry()
    {
        FakeGrantStore store = new(GameLoginTicketGrantStatus.AuthenticationStateChanged);

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 202u],
            authenticationKeys: [303u, 404u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            TestContext.Current.CancellationToken
        );

        Assert.Null(ticket);
        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_StateChangeAfterCollisionStopsFurtherAllocation()
    {
        FakeGrantStore store = new(
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.AuthenticationStateChanged
        );

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 202u, 303u],
            authenticationKeys: [404u, 505u, 606u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            TestContext.Current.CancellationToken
        );

        Assert.Null(ticket);
        Assert.Equal(2, store.GrantCount);
        Assert.Equal(2, tokens.SessionUidGenerationCount);
        Assert.Equal(2, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_EightSessionUidCollisionsExhaustAllocationBudget()
    {
        FakeGrantStore store = new(
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision,
            GameLoginTicketGrantStatus.SessionUidCollision
        );

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 102u, 103u, 104u, 105u, 106u, 107u, 108u],
            authenticationKeys: [201u, 202u, 203u, 204u, 205u, 206u, 207u, 208u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<GameLoginTicketAllocationException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(8, store.GrantCount);
        Assert.Equal(8, tokens.SessionUidGenerationCount);
        Assert.Equal(8, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_PersistenceFailurePropagatesWithoutRetry()
    {
        FakeGrantStore store = new() { Exception = new IOException("Persistence failed.") };

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 202u],
            authenticationKeys: [303u, 404u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAsync<IOException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_UnsupportedGrantStatusFailsClosed()
    {
        FakeGrantStore store = new((GameLoginTicketGrantStatus)99);

        GameLoginTicketIssuer issuer = new(
            store,
            new SequenceTokenGenerator(),
            new FixedTimeProvider(s_issuedAtUtc)
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            issuer
                .IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Equal(1, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_CancellationAfterCollisionIsObservedBeforeRetry()
    {
        using CancellationTokenSource cancellation = new();

        FakeGrantStore store = new(GameLoginTicketGrantStatus.SessionUidCollision)
        {
            OnGrant = cancellation.Cancel,
        };

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 202u],
            authenticationKeys: [303u, 404u]
        );

        GameLoginTicketIssuer issuer = new(store, tokens, new FixedTimeProvider(s_issuedAtUtc));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            issuer.IssueAsync(CreateSuccessfulAuthentication(), cancellation.Token).AsTask()
        );

        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_CancellationAfterDurableGrantDoesNotHideSuccess()
    {
        using CancellationTokenSource cancellation = new();

        FakeGrantStore store = new(GameLoginTicketGrantStatus.Granted)
        {
            OnGrant = cancellation.Cancel,
        };

        GameLoginTicketIssuer issuer = new(
            store,
            new SequenceTokenGenerator(),
            new FixedTimeProvider(s_issuedAtUtc)
        );

        GameLoginTicket? ticket = await issuer.IssueAsync(
            CreateSuccessfulAuthentication(),
            cancellation.Token
        );

        Assert.NotNull(ticket);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.GrantCount);
    }

    private static AccountAuthenticationResult CreateSuccessfulAuthentication()
    {
        return AccountAuthenticationResult.Succeeded(
            AccountId,
            Username,
            AccountStateRevision,
            PasswordCredentialRevision
        );
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private sealed class SequenceTokenGenerator : IGameLoginTicketTokenGenerator
    {
        private readonly Queue<uint> _sessionUids;
        private readonly Queue<uint> _authenticationKeys;

        public SequenceTokenGenerator(
            IEnumerable<uint>? sessionUids = null,
            IEnumerable<uint>? authenticationKeys = null
        )
        {
            _sessionUids = new Queue<uint>(sessionUids ?? [0x1020_3040u]);

            _authenticationKeys = new Queue<uint>(authenticationKeys ?? [0x5060_7080u]);
        }

        public int SessionUidGenerationCount { get; private set; }

        public int AuthenticationKeyGenerationCount { get; private set; }

        public uint GenerateSessionUid()
        {
            SessionUidGenerationCount++;

            if (_sessionUids.Count == 0)
            {
                throw new InvalidOperationException("No test session UID remains.");
            }

            return _sessionUids.Dequeue();
        }

        public uint GenerateAuthenticationKey()
        {
            AuthenticationKeyGenerationCount++;

            if (_authenticationKeys.Count == 0)
            {
                throw new InvalidOperationException("No test authentication key remains.");
            }

            return _authenticationKeys.Dequeue();
        }
    }

    private sealed class FakeGrantStore : IGameLoginTicketGrantStore
    {
        private readonly Queue<GameLoginTicketGrantStatus> _results;

        public FakeGrantStore(params GameLoginTicketGrantStatus[] results)
        {
            _results = new Queue<GameLoginTicketGrantStatus>(
                results.Length == 0 ? [GameLoginTicketGrantStatus.Granted] : results
            );
        }

        public Exception? Exception { get; init; }

        public Action? OnGrant { get; init; }

        public int GrantCount { get; private set; }

        public List<GameLoginTicket> GrantedTickets { get; } = [];

        public List<ulong> AccountStateRevisions { get; } = [];

        public List<ulong> PasswordCredentialRevisions { get; } = [];

        public ValueTask<GameLoginTicketGrantStatus> TryGrantAsync(
            GameLoginTicket ticket,
            ulong expectedAccountStateRevision,
            ulong expectedPasswordCredentialRevision,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(ticket);

            cancellationToken.ThrowIfCancellationRequested();

            GrantCount++;
            GrantedTickets.Add(ticket);
            AccountStateRevisions.Add(expectedAccountStateRevision);
            PasswordCredentialRevisions.Add(expectedPasswordCredentialRevision);

            OnGrant?.Invoke();

            if (Exception is not null)
            {
                throw Exception;
            }

            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No test grant result remains.");
            }

            return ValueTask.FromResult(_results.Dequeue());
        }
    }
}
