using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;

namespace OpenConquer.Application.Tests.Accounts.GameLogin;

public sealed class GameLoginTicketIssuerTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const ulong AccountStateRevision = 7;
    private const ulong PasswordCredentialRevision = 11;

    private static readonly DateTimeOffset s_durableIssuedAtUtc = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_RejectsNullGrantStore()
    {
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketIssuer(null!, new SequenceTokenGenerator()));
    }

    [Fact]
    public void Constructor_RejectsNullTokenGenerator()
    {
        Assert.Throws<ArgumentNullException>(() => new GameLoginTicketIssuer(new FakeGrantStore(), null!));
    }

    [Fact]
    public async Task IssueAsync_RejectsUnsuccessfulAuthentication()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<ArgumentException>(() => issuer.IssueAsync(AccountAuthenticationResult.InvalidCredentials(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_RejectsBannedAuthentication()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new();
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<ArgumentException>(() => issuer.IssueAsync(AccountAuthenticationResult.Banned(), TestContext.Current.CancellationToken).AsTask());

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
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), cancellation.Token).AsTask());

        Assert.Equal(0, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_GrantedTicketUsesDurablePersistenceResultAndExpectedPolicy()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new(sessionUids: [0x1020_3040u], authenticationKeys: [0x5060_7080u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        GameLoginTicket? ticket = await issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken);

        GameLoginTicket granted = Assert.IsType<GameLoginTicket>(ticket);
        GameLoginTicketGrantRequest request = Assert.Single(store.Requests);

        Assert.Equal(AccountId, request.AccountId);
        Assert.Equal(Username, request.Username);
        Assert.Equal(0x1020_3040u, request.SessionUid);
        Assert.Equal(0x5060_7080u, request.AuthenticationKey);

        Assert.Equal(AccountId, granted.AccountId);
        Assert.Equal(Username, granted.Username);
        Assert.Equal(0x1020_3040u, granted.SessionUid);
        Assert.Equal(0x5060_7080u, granted.AuthenticationKey);
        Assert.Equal(s_durableIssuedAtUtc, granted.IssuedAtUtc);
        Assert.Equal(s_durableIssuedAtUtc.AddMinutes(5), granted.ExpiresAtUtc);

        Assert.Equal(1, store.GrantCount);
        Assert.Equal(TimeSpan.FromMinutes(5), Assert.Single(store.TicketLifetimes));
        Assert.Equal(AccountStateRevision, Assert.Single(store.AccountStateRevisions));
        Assert.Equal(PasswordCredentialRevision, Assert.Single(store.PasswordCredentialRevisions));
        Assert.Same(granted, Assert.Single(store.DurableTickets));
    }

    [Fact]
    public async Task IssueAsync_RejectsZeroSessionUidFromGenerator()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new(sessionUids: [0u], authenticationKeys: [123u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(0, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_RejectsZeroAuthenticationKeyFromGenerator()
    {
        FakeGrantStore store = new();
        SequenceTokenGenerator tokens = new(sessionUids: [123u], authenticationKeys: [0u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(0, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_SessionUidCollisionGeneratesEntirelyNewCredentialPair()
    {
        FakeGrantStore store = new(GameLoginTicketGrantStatus.SessionUidCollision, GameLoginTicketGrantStatus.Granted);
        SequenceTokenGenerator tokens = new(sessionUids: [101u, 202u], authenticationKeys: [303u, 404u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        GameLoginTicket? ticket = await issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken);

        GameLoginTicket granted = Assert.IsType<GameLoginTicket>(ticket);

        Assert.Equal(2, store.GrantCount);
        Assert.Equal(2, tokens.SessionUidGenerationCount);
        Assert.Equal(2, tokens.AuthenticationKeyGenerationCount);
        Assert.Equal(2, store.Requests.Count);

        Assert.Equal(101u, store.Requests[0].SessionUid);
        Assert.Equal(303u, store.Requests[0].AuthenticationKey);
        Assert.Equal(202u, store.Requests[1].SessionUid);
        Assert.Equal(404u, store.Requests[1].AuthenticationKey);

        Assert.Equal(202u, granted.SessionUid);
        Assert.Equal(404u, granted.AuthenticationKey);
        Assert.Same(granted, Assert.Single(store.DurableTickets));
    }

    [Fact]
    public async Task IssueAsync_AuthenticationStateChangedReturnsNullWithoutRetry()
    {
        FakeGrantStore store = new(GameLoginTicketGrantStatus.AuthenticationStateChanged);
        SequenceTokenGenerator tokens = new(sessionUids: [101u, 202u], authenticationKeys: [303u, 404u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        GameLoginTicket? ticket = await issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken);

        Assert.Null(ticket);
        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_StateChangeAfterCollisionStopsFurtherAllocation()
    {
        FakeGrantStore store = new(GameLoginTicketGrantStatus.SessionUidCollision, GameLoginTicketGrantStatus.AuthenticationStateChanged);
        SequenceTokenGenerator tokens = new(sessionUids: [101u, 202u, 303u], authenticationKeys: [404u, 505u, 606u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        GameLoginTicket? ticket = await issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken);

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
            GameLoginTicketGrantStatus.SessionUidCollision);

        SequenceTokenGenerator tokens = new(
            sessionUids: [101u, 102u, 103u, 104u, 105u, 106u, 107u, 108u],
            authenticationKeys: [201u, 202u, 203u, 204u, 205u, 206u, 207u, 208u]);

        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<GameLoginTicketAllocationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(8, store.GrantCount);
        Assert.Equal(8, tokens.SessionUidGenerationCount);
        Assert.Equal(8, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_PersistenceFailurePropagatesWithoutRetry()
    {
        FakeGrantStore store = new() { Exception = new IOException("Persistence failed.") };
        SequenceTokenGenerator tokens = new(sessionUids: [101u, 202u], authenticationKeys: [303u, 404u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAsync<IOException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_UnsupportedGrantStatusFailsClosed()
    {
        FakeGrantStore store = new() { ResultFactory = static (_, _) => default };
        GameLoginTicketIssuer issuer = new(store, new SequenceTokenGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_GrantedTicketWithMismatchedCredentialsFailsClosed()
    {
        FakeGrantStore store = new()
        {
            ResultFactory = static (request, lifetime) =>
            {
                GameLoginTicket ticket = new(request.AccountId, request.Username, request.SessionUid + 1, request.AuthenticationKey, s_durableIssuedAtUtc, s_durableIssuedAtUtc.Add(lifetime));
                return GameLoginTicketGrantResult.Granted(ticket);
            },
        };

        GameLoginTicketIssuer issuer = new(store, new SequenceTokenGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_GrantedTicketWithUnexpectedLifetimeFailsClosed()
    {
        FakeGrantStore store = new()
        {
            ResultFactory = static (request, _) =>
            {
                GameLoginTicket ticket = new(request.AccountId, request.Username, request.SessionUid, request.AuthenticationKey, s_durableIssuedAtUtc, s_durableIssuedAtUtc.AddMinutes(4));
                return GameLoginTicketGrantResult.Granted(ticket);
            },
        };

        GameLoginTicketIssuer issuer = new(store, new SequenceTokenGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, store.GrantCount);
    }

    [Fact]
    public async Task IssueAsync_CancellationAfterCollisionIsObservedBeforeRetry()
    {
        using CancellationTokenSource cancellation = new();

        FakeGrantStore store = new(GameLoginTicketGrantStatus.SessionUidCollision) { OnGrant = cancellation.Cancel };
        SequenceTokenGenerator tokens = new(sessionUids: [101u, 202u], authenticationKeys: [303u, 404u]);
        GameLoginTicketIssuer issuer = new(store, tokens);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => issuer.IssueAsync(CreateSuccessfulAuthentication(), cancellation.Token).AsTask());

        Assert.Equal(1, store.GrantCount);
        Assert.Equal(1, tokens.SessionUidGenerationCount);
        Assert.Equal(1, tokens.AuthenticationKeyGenerationCount);
    }

    [Fact]
    public async Task IssueAsync_CancellationAfterDurableGrantDoesNotHideSuccess()
    {
        using CancellationTokenSource cancellation = new();

        FakeGrantStore store = new(GameLoginTicketGrantStatus.Granted) { OnGrant = cancellation.Cancel };
        GameLoginTicketIssuer issuer = new(store, new SequenceTokenGenerator());

        GameLoginTicket? ticket = await issuer.IssueAsync(CreateSuccessfulAuthentication(), cancellation.Token);

        Assert.NotNull(ticket);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.GrantCount);
    }

    private static AccountAuthenticationResult CreateSuccessfulAuthentication()
    {
        return AccountAuthenticationResult.Succeeded(AccountId, Username, AccountStateRevision, PasswordCredentialRevision);
    }

    private sealed class SequenceTokenGenerator : IGameLoginTicketTokenGenerator
    {
        private readonly Queue<uint> _sessionUids;
        private readonly Queue<uint> _authenticationKeys;

        public SequenceTokenGenerator(IEnumerable<uint>? sessionUids = null, IEnumerable<uint>? authenticationKeys = null)
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
        private readonly Queue<GameLoginTicketGrantStatus> _statuses;

        public FakeGrantStore(params GameLoginTicketGrantStatus[] statuses)
        {
            _statuses = new Queue<GameLoginTicketGrantStatus>(statuses.Length == 0 ? [GameLoginTicketGrantStatus.Granted] : statuses);
        }

        public Exception? Exception { get; init; }
        public Action? OnGrant { get; init; }
        public Func<GameLoginTicketGrantRequest, TimeSpan, GameLoginTicketGrantResult>? ResultFactory { get; init; }

        public int GrantCount { get; private set; }
        public List<GameLoginTicketGrantRequest> Requests { get; } = [];
        public List<GameLoginTicket> DurableTickets { get; } = [];
        public List<TimeSpan> TicketLifetimes { get; } = [];
        public List<ulong> AccountStateRevisions { get; } = [];
        public List<ulong> PasswordCredentialRevisions { get; } = [];

        public ValueTask<GameLoginTicketGrantResult> TryGrantAsync(GameLoginTicketGrantRequest request, TimeSpan ticketLifetime, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            GrantCount++;
            Requests.Add(request);
            TicketLifetimes.Add(ticketLifetime);
            AccountStateRevisions.Add(expectedAccountStateRevision);
            PasswordCredentialRevisions.Add(expectedPasswordCredentialRevision);

            OnGrant?.Invoke();

            if (Exception is not null)
            {
                throw Exception;
            }

            if (ResultFactory is not null)
            {
                return ValueTask.FromResult(ResultFactory(request, ticketLifetime));
            }

            if (_statuses.Count == 0)
            {
                throw new InvalidOperationException("No test grant result remains.");
            }

            GameLoginTicketGrantStatus status = _statuses.Dequeue();

            switch (status)
            {
                case GameLoginTicketGrantStatus.Granted:
                    {
                        GameLoginTicket ticket = new(request.AccountId, request.Username, request.SessionUid, request.AuthenticationKey, s_durableIssuedAtUtc, s_durableIssuedAtUtc.Add(ticketLifetime));
                        DurableTickets.Add(ticket);
                        return ValueTask.FromResult(GameLoginTicketGrantResult.Granted(ticket));
                    }

                case GameLoginTicketGrantStatus.SessionUidCollision:
                    return ValueTask.FromResult(GameLoginTicketGrantResult.SessionUidCollision());

                case GameLoginTicketGrantStatus.AuthenticationStateChanged:
                    return ValueTask.FromResult(GameLoginTicketGrantResult.AuthenticationStateChanged());

                default:
                    return ValueTask.FromResult(default(GameLoginTicketGrantResult));
            }
        }
    }
}
