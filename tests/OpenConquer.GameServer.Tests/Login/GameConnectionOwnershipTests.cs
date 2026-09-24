using OpenConquer.Application.Characters.Login;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login;
using OpenConquer.GameServer.Tests.Connections;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class GameConnectionOwnershipTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;

    [Fact]
    public async Task AuthenticationResult_DisposeWithoutTransferDisposesOwnedConnection()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult owner = GameConnectionAuthenticationResult.Authenticated(connection);

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task AuthenticationResult_ConcurrentTakeAllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult owner = GameConnectionAuthenticationResult.Authenticated(connection);

        AuthenticatedGameConnection? first = null;
        AuthenticatedGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        AssertOneTakeSucceeded(connection, first, firstFailure, second, secondFailure);
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task AuthenticationResult_TakeAndDisposeRaceAllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        GameConnectionAuthenticationResult owner = GameConnectionAuthenticationResult.Authenticated(connection);

        AuthenticatedGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(
            transferred is not null ^ takeFailure is InvalidOperationException,
            $"Expected exactly one ownership winner. Transferred: {transferred is not null}, failure: {takeFailure?.GetType().Name ?? "none"}.");

        if (transferred is not null)
        {
            Assert.Same(connection, transferred);
            Assert.Equal(0, transport.DisposeCount);

            await transferred.DisposeAsync();
        }
        else
        {
            Assert.Equal(1, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task CharacterLoginHandoff_DisposeWithoutTransferDisposesOwnedConnection()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginHandoffResult owner = new(connection, CharacterLoginResolution.CharacterCreation());

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task CharacterLoginHandoff_ConcurrentTakeAllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginHandoffResult owner = new(connection, CharacterLoginResolution.CharacterCreation());

        AuthenticatedGameConnection? first = null;
        AuthenticatedGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        AssertOneTakeSucceeded(connection, first, firstFailure, second, secondFailure);
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task CharacterLoginHandoff_TakeAndDisposeRaceAllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginHandoffResult owner = new(connection, CharacterLoginResolution.CharacterCreation());

        AuthenticatedGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(
            transferred is not null ^ takeFailure is InvalidOperationException,
            $"Expected exactly one ownership winner. Transferred: {transferred is not null}, failure: {takeFailure?.GetType().Name ?? "none"}.");

        if (transferred is not null)
        {
            Assert.Same(connection, transferred);
            Assert.Equal(0, transport.DisposeCount);

            await transferred.DisposeAsync();
        }
        else
        {
            Assert.Equal(1, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task AwaitingEnterMap_DisposeWithoutTransferDisposesOwnedConnection()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        AwaitingEnterMapConnection owner = new(connection, CreateProfile());

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task AwaitingEnterMap_ConcurrentTakeAllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        AwaitingEnterMapConnection owner = new(connection, CreateProfile());

        AuthenticatedGameConnection? first = null;
        AuthenticatedGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        AssertOneTakeSucceeded(connection, first, firstFailure, second, secondFailure);
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task AwaitingEnterMap_TakeAndDisposeRaceAllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        AwaitingEnterMapConnection owner = new(connection, CreateProfile());

        AuthenticatedGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(
            transferred is not null ^ takeFailure is InvalidOperationException,
            $"Expected exactly one ownership winner. Transferred: {transferred is not null}, failure: {takeFailure?.GetType().Name ?? "none"}.");

        if (transferred is not null)
        {
            Assert.Same(connection, transferred);
            Assert.Equal(0, transport.DisposeCount);

            await transferred.DisposeAsync();
        }
        else
        {
            Assert.Equal(1, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task EnteredMap_DisposeWithoutTransferDisposesOwnedConnection()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        EnteredMapConnection owner = new(connection, CreateProfile(), CreateMap());

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task EnteredMap_ConcurrentTakeAllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        EnteredMapConnection owner = new(connection, CreateProfile(), CreateMap());

        AuthenticatedGameConnection? first = null;
        AuthenticatedGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        AssertOneTakeSucceeded(connection, first, firstFailure, second, secondFailure);
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task EnteredMap_TakeAndDisposeRaceAllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        EnteredMapConnection owner = new(connection, CreateProfile(), CreateMap());

        AuthenticatedGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(
            transferred is not null ^ takeFailure is InvalidOperationException,
            $"Expected exactly one ownership winner. Transferred: {transferred is not null}, failure: {takeFailure?.GetType().Name ?? "none"}.");

        if (transferred is not null)
        {
            Assert.Same(connection, transferred);
            Assert.Equal(0, transport.DisposeCount);

            await transferred.DisposeAsync();
        }
        else
        {
            Assert.Equal(1, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    private static void TryTake(GameConnectionAuthenticationResult owner, out AuthenticatedGameConnection? connection, out Exception? failure)
    {
        TryTake(owner.TakeConnection, out connection, out failure);
    }

    private static void TryTake(CharacterLoginHandoffResult owner, out AuthenticatedGameConnection? connection, out Exception? failure)
    {
        TryTake(owner.TakeConnection, out connection, out failure);
    }

    private static void TryTake(AwaitingEnterMapConnection owner, out AuthenticatedGameConnection? connection, out Exception? failure)
    {
        TryTake(owner.TakeConnection, out connection, out failure);
    }

    private static void TryTake(EnteredMapConnection owner, out AuthenticatedGameConnection? connection, out Exception? failure)
    {
        TryTake(owner.TakeConnection, out connection, out failure);
    }

    private static void TryTake(Func<AuthenticatedGameConnection> take, out AuthenticatedGameConnection? connection, out Exception? failure)
    {
        try
        {
            connection = take();
            failure = null;
        }
        catch (Exception exception)
        {
            connection = null;
            failure = exception;
        }
    }

    private static void AssertOneTakeSucceeded(
        AuthenticatedGameConnection expected,
        AuthenticatedGameConnection? first,
        Exception? firstFailure,
        AuthenticatedGameConnection? second,
        Exception? secondFailure)
    {
        Assert.True(
            first is not null ^ second is not null,
            $"Expected exactly one successful transfer. First: {first is not null}, second: {second is not null}.");

        Assert.Same(expected, first ?? second);

        Exception? failure = firstFailure ?? secondFailure;
        Assert.IsType<InvalidOperationException>(failure);

        Assert.True(
            firstFailure is null ^ secondFailure is null,
            $"Expected exactly one failed transfer. First failure: {firstFailure?.GetType().Name ?? "none"}, second failure: {secondFailure?.GetType().Name ?? "none"}.");
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: 0, titleId: 0, enlightenmentPoints: 0, location);
    }

    private static GameMapEntryDefinition CreateMap() => new(mapId: 1002, mapDataId: 1015, flags: 0);
}
