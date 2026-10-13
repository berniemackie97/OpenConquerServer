using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
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

        Assert.True(transferred is not null ^ takeFailure is InvalidOperationException,
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

        Assert.True(transferred is not null ^ takeFailure is InvalidOperationException,
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

    private static void TryTake(GameConnectionAuthenticationResult owner, out AuthenticatedGameConnection? connection, out Exception? failure) => TryTake(owner.TakeConnection, out connection, out failure);

    private static void TryTake(CharacterLoginHandoffResult owner, out AuthenticatedGameConnection? connection, out Exception? failure) => TryTake(owner.TakeConnection, out connection, out failure);

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

    private static void AssertOneTakeSucceeded(AuthenticatedGameConnection expected, AuthenticatedGameConnection? first, Exception? firstFailure, AuthenticatedGameConnection? second, Exception? secondFailure)
    {
        Assert.True(first is not null ^ second is not null,
            $"Expected exactly one successful transfer. First: {first is not null}, second: {second is not null}.");
        Assert.Same(expected, first ?? second);
        Assert.IsType<InvalidOperationException>(firstFailure ?? secondFailure);
        Assert.True(firstFailure is null ^ secondFailure is null,
            $"Expected exactly one failed transfer. First failure: {firstFailure?.GetType().Name ?? "none"}, second failure: {secondFailure?.GetType().Name ?? "none"}.");
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }
}
