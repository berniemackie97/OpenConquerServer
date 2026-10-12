using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Authentication;

public sealed class AuthenticatedGameConnectionRepeatedProofTests
{
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;

    [Fact]
    public async Task TryConsumeRepeatedLoginProof_UnboundConnectionFailsClosed()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();

        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
    }

    [Fact]
    public async Task TryConsumeRepeatedLoginProof_CorrectProofSucceedsExactlyOnce()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();
        connection.ArmRepeatedLoginProof(CreateProof());

        Assert.True(connection.TryConsumeRepeatedLoginProof(CreateProof()));
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
    }

    [Theory]
    [InlineData(0x1020_3041u, AuthenticationKey)]
    [InlineData(SessionUid, 0x5060_7081u)]
    [InlineData(0u, AuthenticationKey)]
    [InlineData(SessionUid, 0u)]
    public async Task TryConsumeRepeatedLoginProof_IncorrectIdentityBurnsTheAttempt(uint sessionUid, uint authenticationKey)
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();
        connection.ArmRepeatedLoginProof(CreateProof());

        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof(sessionUid, authenticationKey)));
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
    }

    [Fact]
    public async Task TryConsumeRepeatedLoginProof_InvalidModeBurnsTheAttempt()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();
        connection.ArmRepeatedLoginProof(CreateProof());

        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof() with { Mode = 0 }));
        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
    }

    [Fact]
    public async Task ArmRepeatedLoginProof_RejectsMismatchedOrZeroInitialProof()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();

        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof(SessionUid, 0)));
        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof(SessionUid + 1, AuthenticationKey)));
        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof() with { LocaleTag = 0 }));
        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof() with { HardwareAddress = 0 }));
        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof() with { ResourceVersion = 0 }));
        Assert.Throws<ArgumentException>(() => connection.ArmRepeatedLoginProof(CreateProof() with { Mode = 0 }));

        connection.ArmRepeatedLoginProof(CreateProof());

        Assert.Throws<InvalidOperationException>(() => connection.ArmRepeatedLoginProof(CreateProof()));
        Assert.True(connection.TryConsumeRepeatedLoginProof(CreateProof()));
    }

    [Fact]
    public async Task DiscardRepeatedLoginProof_CannotBeRearmedOrConsumed()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();
        connection.ArmRepeatedLoginProof(CreateProof());

        connection.DiscardRepeatedLoginProof();

        Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
        Assert.Throws<InvalidOperationException>(() => connection.ArmRepeatedLoginProof(CreateProof()));
    }

    [Fact]
    public async Task DisposeAsync_RevokesRepeatedProofBeforeClosingTransport()
    {
        FakeGameTransportConnection transport = new(blockDispose: true);
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        connection.ArmRepeatedLoginProof(CreateProof());

        Task disposal = connection.DisposeAsync().AsTask();

        try
        {
            await transport.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            Assert.False(connection.TryConsumeRepeatedLoginProof(CreateProof()));
        }
        finally
        {
            transport.ReleaseDispose();
            await disposal.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TryConsumeRepeatedLoginProof_ConcurrentAttemptsHaveOneWinner()
    {
        await using AuthenticatedGameConnection connection = await CreateConnectionAsync();
        connection.ArmRepeatedLoginProof(CreateProof());

        Task<bool>[] attempts = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => connection.TryConsumeRepeatedLoginProof(CreateProof())))
            .ToArray();

        bool[] results = await Task.WhenAll(attempts);

        Assert.Single(results, succeeded => succeeded);
    }

    private static GameLoginProof CreateProof(uint sessionUid = SessionUid, uint authenticationKey = AuthenticationKey)
    {
        return new GameLoginProof(sessionUid, authenticationKey, GameLoginProof.ExpectedMode, LocaleTag, HardwareAddress, ResourceVersion);
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection? transport = null)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport ?? new FakeGameTransportConnection(), TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(42, "Bernie", SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }
}
