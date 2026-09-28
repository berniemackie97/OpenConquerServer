using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character.Resolution;
using OpenConquer.GameServer.Tests.Connections;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class CharacterLoginHandoffProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;

    [Fact]
    public void Constructor_NullResolverIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CharacterLoginHandoffProcessor(null!));
    }

    [Fact]
    public async Task ProcessAsync_CharacterCreation_TransfersSameLiveConnectionExactlyOnce()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(CharacterLoginResolution.CharacterCreation());
        CharacterLoginHandoffProcessor processor = new(resolver);

        {
            await using CharacterLoginHandoffResult result = await processor.ProcessAsync(authentication, TestContext.Current.CancellationToken);
            await using AuthenticatedGameConnection transferredConnection = result.TakeConnection();

            Assert.Same(connection, transferredConnection);
            Assert.Equal(CharacterLoginRoute.CharacterCreation, result.Resolution.Route);
            Assert.Null(result.Resolution.Profile);
            Assert.Equal(AccountId, resolver.LastAccountId);
            Assert.Equal(1, resolver.ResolveCallCount);
            Assert.Equal(0, transport.DisposeCount);
            Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_ExistingCharacter_TransfersSameLiveConnectionAndProfileExactlyOnce()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        CharacterLoginProfile profile = CreateProfile(AccountId);
        StubResolver resolver = new(CharacterLoginResolution.ExistingCharacter(profile));
        CharacterLoginHandoffProcessor processor = new(resolver);

        {
            await using CharacterLoginHandoffResult result = await processor.ProcessAsync(authentication, TestContext.Current.CancellationToken);
            await using AuthenticatedGameConnection transferredConnection = result.TakeConnection();

            Assert.Same(connection, transferredConnection);
            Assert.Equal(CharacterLoginRoute.ExistingCharacter, result.Resolution.Route);
            Assert.Same(profile, result.Resolution.Profile);
            Assert.Equal(AccountId, resolver.LastAccountId);
            Assert.Equal(1, resolver.ResolveCallCount);
            Assert.Equal(0, transport.DisposeCount);
            Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfAuthenticationResultIsRejectedBeforeSecondResolution()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(CharacterLoginResolution.CharacterCreation());
        CharacterLoginHandoffProcessor processor = new(resolver);

        {
            await using CharacterLoginHandoffResult result = await processor.ProcessAsync(authentication, TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(1, resolver.ResolveCallCount);
            Assert.Equal(0, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfAuthenticationResultAllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        BlockingResolver resolver = new(CharacterLoginResolution.CharacterCreation());
        CharacterLoginHandoffProcessor processor = new(resolver);

        Task<CharacterLoginHandoffResult> first = processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask();

        await resolver.Entered.WaitAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, resolver.ResolveCallCount);
        Assert.Equal(0, transport.DisposeCount);

        resolver.Release();

        {
            await using CharacterLoginHandoffResult result = await first;

            Assert.Equal(CharacterLoginRoute.CharacterCreation, result.Resolution.Route);
            Assert.Equal(0, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_DisposedAuthenticationResultIsRejectedWithoutInvokingResolver()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(CharacterLoginResolution.CharacterCreation());
        CharacterLoginHandoffProcessor processor = new(resolver);

        await authentication.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, resolver.ResolveCallCount);
        Assert.Null(resolver.LastAccountId);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_UnspecifiedResolutionIsRejectedAndConnectionDisposed()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(default);
        CharacterLoginHandoffProcessor processor = new(resolver);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AccountId, resolver.LastAccountId);
        Assert.Equal(1, resolver.ResolveCallCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_ProfileBelongsToDifferentAccountIsRejectedAndConnectionDisposed()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(CharacterLoginResolution.ExistingCharacter(CreateProfile(accountId: 99)));
        CharacterLoginHandoffProcessor processor = new(resolver);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(AccountId, resolver.LastAccountId);
        Assert.Equal(1, resolver.ResolveCallCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_ResolverFailurePropagatesAndConnectionDisposed()
    {
        InvalidOperationException processingFailure = new("character resolution failed");
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(default, processingFailure);
        CharacterLoginHandoffProcessor processor = new(resolver);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(processingFailure, exception);
        Assert.Equal(AccountId, resolver.LastAccountId);
        Assert.Equal(1, resolver.ResolveCallCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperationConsumesAndDisposesConnectionWithoutInvokingResolver()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(CharacterLoginResolution.CharacterCreation());
        CharacterLoginHandoffProcessor processor = new(resolver);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessAsync(authentication, cancellation.Token).AsTask());

        Assert.Null(resolver.LastAccountId);
        Assert.Equal(0, resolver.ResolveCallCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_ResolutionFailureAndCleanupFailureAreAggregated()
    {
        InvalidOperationException processingFailure = new("character resolution failed");
        IOException cleanupFailure = new("transport dispose failed");
        FakeGameTransportConnection transport = new(disposeFailure: cleanupFailure);
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        StubResolver resolver = new(default, processingFailure);
        CharacterLoginHandoffProcessor processor = new(resolver);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            processor.ProcessAsync(authentication, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(processingFailure, exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(AccountId, resolver.LastAccountId);
        Assert.Equal(1, resolver.ResolveCallCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }

    private static CharacterLoginProfile CreateProfile(uint accountId)
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, accountId, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: 0, titleId: 0, enlightenmentPoints: 0, location);
    }

    private sealed class StubResolver(CharacterLoginResolution result, Exception? failure = null) : ICharacterLoginResolver
    {
        public int ResolveCallCount { get; private set; }
        public uint? LastAccountId { get; private set; }

        public ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            LastAccountId = accountId;
            cancellationToken.ThrowIfCancellationRequested();

            if (failure is not null)
            {
                throw failure;
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingResolver(CharacterLoginResolution result) : ICharacterLoginResolver
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public int ResolveCallCount { get; private set; }

        public async ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            _entered.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);

            return result;
        }

        public void Release() => _release.TrySetResult();
    }
}
