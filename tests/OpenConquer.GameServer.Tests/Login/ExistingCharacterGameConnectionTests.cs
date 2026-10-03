using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterGameConnectionTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint OtherCharacterId = CharacterId + 1;

    [Fact]
    public async Task Constructor_NullDependencies_AreRejected()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginProfile profile = CreateProfile();
        CharacterPresenceDirectory directory = new();
        using ICharacterPresenceLease presence = directory.Register(CharacterId);

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterGameConnection(null!, profile, presence));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterGameConnection(connection, null!, presence));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterGameConnection(connection, profile, null!));

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_ProfileBelongsToDifferentAccount_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        using ICharacterPresenceLease presence = directory.Register(CharacterId);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new ExistingCharacterGameConnection(connection, CreateProfile(accountId: AccountId + 1), presence));

        Assert.Equal("profile", exception.ParamName);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_PresenceBelongsToDifferentCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        using ICharacterPresenceLease presence = directory.Register(OtherCharacterId);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new ExistingCharacterGameConnection(connection, CreateProfile(), presence));

        Assert.Equal("presenceLease", exception.ParamName);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_ValidOwnershipExposesCanonicalProfileAndActivePresence()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginProfile profile = CreateProfile();
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, profile, presence);

        Assert.Same(profile, owner.Profile);
        Assert.False(owner.IsRevoked);
        Assert.False(owner.RevocationToken.IsCancellationRequested);
        Assert.True(directory.IsOnline(CharacterId));

        owner.ThrowIfRevoked();

        await owner.DisposeAsync();

        Assert.False(directory.IsOnline(CharacterId));
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReplacementPresence_RevokesOldOwnerWithoutAllowingStaleCleanupToRemoveReplacement()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        using ICharacterPresenceLease replacement = directory.Register(CharacterId);

        Assert.True(owner.IsRevoked);
        Assert.True(owner.RevocationToken.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => owner.ThrowIfRevoked());
        Assert.True(directory.IsOnline(CharacterId));

        await owner.DisposeAsync();

        Assert.True(directory.IsOnline(CharacterId));
        Assert.False(replacement.IsRevoked);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReadAsync_PresenceReplacementCancelsActiveRead()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateSecuredConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        Task readTask = owner.ReadAsync(TestContext.Current.CancellationToken).AsTask();

        Assert.False(readTask.IsCompleted);

        using ICharacterPresenceLease replacement = directory.Register(CharacterId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);

        Assert.True(owner.IsRevoked);
        Assert.True(directory.IsOnline(CharacterId));

        await owner.DisposeAsync();

        Assert.True(directory.IsOnline(CharacterId));
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReadAsync_CallerCancellationDoesNotRevokePresence()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateSecuredConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.ReadAsync(cancellation.Token).AsTask());

        Assert.False(owner.IsRevoked);
        Assert.True(directory.IsOnline(CharacterId));

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task WriteAsync_PresenceReplacementCancelsActiveWriteAndCleanupPreventsPublication()
    {
        FakeGameTransportConnection transport = new();
        AuthenticatedGameConnection connection = await CreateSecuredConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);
        int boundary = transport.SentBytes.Length;

        transport.BlockSends();

        Task writeTask = owner.WriteAsync(new GameServerStatePacket2079(1), TestContext.Current.CancellationToken).AsTask();

        await transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        using ICharacterPresenceLease replacement = directory.Register(CharacterId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writeTask);
        await owner.DisposeAsync();

        Assert.Equal(boundary, transport.SentBytes.Length);
        Assert.True(directory.IsOnline(CharacterId));
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesPresenceBeforeTransportDisposalCompletes()
    {
        FakeGameTransportConnection transport = new(blockDispose: true);
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        Task disposal = owner.DisposeAsync().AsTask();

        await transport.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(directory.IsOnline(CharacterId));
        Assert.True(owner.IsRevoked);
        Assert.Throws<ObjectDisposedException>(() => owner.ThrowIfRevoked());
        Assert.False(disposal.IsCompleted);

        transport.ReleaseDispose();

        await disposal;

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallersShareSameCleanup()
    {
        FakeGameTransportConnection transport = new(blockDispose: true);
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        Task first = owner.DisposeAsync().AsTask();

        await transport.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Task second = owner.DisposeAsync().AsTask();

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, transport.DisposeCount);

        transport.ReleaseDispose();

        await first;

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_RejectsNewOperationsAfterDisposalBegins()
    {
        FakeGameTransportConnection transport = new(blockDispose: true);
        AuthenticatedGameConnection connection = await CreateSecuredConnectionAsync(transport);
        CharacterPresenceDirectory directory = new();
        ICharacterPresenceLease presence = directory.Register(CharacterId);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        Task disposal = owner.DisposeAsync().AsTask();

        await transport.DisposeStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Throws<ObjectDisposedException>(() => owner.ThrowIfRevoked());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.ReadAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.WriteAsync(new GameServerStatePacket2079(1), TestContext.Current.CancellationToken).AsTask());

        transport.ReleaseDispose();

        await disposal;

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_PresenceAndConnectionFailuresAreAggregated()
    {
        IOException presenceFailure = new("presence disposal failed");
        IOException connectionFailure = new("transport disposal failed");
        FakeGameTransportConnection transport = new(disposeFailure: connectionFailure);
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        FakePresenceLease presence = new(CharacterId, presenceFailure);
        ExistingCharacterGameConnection owner = new(connection, CreateProfile(), presence);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAsync().AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(presenceFailure, exception.InnerExceptions[0]);
        Assert.Same(connectionFailure, exception.InnerExceptions[1]);
        Assert.True(presence.IsRevoked);
        Assert.Equal(1, transport.DisposeCount);
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }

    private static async Task<AuthenticatedGameConnection> CreateSecuredConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);

        try
        {
            await CompleteHandshakeAsync(session, transport);
            return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task CompleteHandshakeAsync(GameConnectionSession session, FakeGameTransportConnection transport)
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        using GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, TestContext.Current.CancellationToken);

        transport.QueueReceive(client.EncryptedKeyExchangeResponse);

        string clientPublicKeyHex = Assert.IsType<string>(await session.ReadClientKeyExchangeResponseAsync(TestContext.Current.CancellationToken));
        GameSessionCipher? sessionCipher = exchange.Complete(clientPublicKeyHex);

        try
        {
            session.CompleteHandshake(sessionCipher);
            sessionCipher = null;
        }
        finally
        {
            sessionCipher?.Dispose();
        }
    }

    private static CharacterLoginProfile CreateProfile(uint accountId = AccountId, uint characterId = CharacterId)
    {
        CharacterLoginIdentity identity = new(characterId, accountId, Username);
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25, titleId: 321, enlightenmentPoints: 1234, location);
    }

    private sealed class FakePresenceLease : ICharacterPresenceLease
    {
        private readonly CancellationTokenSource _revocation = new();
        private readonly Exception? _disposeFailure;
        private int _disposed;
        private int _revoked;

        public FakePresenceLease(uint characterId, Exception? disposeFailure = null)
        {
            CharacterId = characterId;
            _disposeFailure = disposeFailure;
            RevocationToken = _revocation.Token;
        }

        public uint CharacterId { get; }
        public bool IsRevoked => Volatile.Read(ref _revoked) != 0;
        public CancellationToken RevocationToken { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _revoked, 1);
            _revocation.Cancel();
            _revocation.Dispose();

            if (_disposeFailure is not null)
            {
                throw _disposeFailure;
            }
        }
    }
}
