using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Accounts.GameLogin.Redemption;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap;

public sealed class ExistingCharacterBootstrapProcessorTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;
    private const ulong MapFlags = 0x1122334455667788;

    private static readonly IPAddress s_remoteAddress = IPAddress.Parse("192.0.2.44");
    private static readonly GameLoginMetrics s_metrics = new();

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        WorldMapContentCatalog catalog = CreateCatalog();

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapProcessor(null!, TestAccountSessions.Create(), catalog, s_metrics));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapProcessor(new CharacterPresenceDirectory(), null!, catalog, s_metrics));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapProcessor(new CharacterPresenceDirectory(), TestAccountSessions.Create(), null!, s_metrics));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapProcessor(new CharacterPresenceDirectory(), TestAccountSessions.Create(), catalog, null!));
    }

    [Fact]
    public async Task ProcessAsync_FenceAcquisitionDenied_ClosesConnectionBeforeAnySuccessPacket()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        RefusingLocationFence fence = new();
        FencedCharacterAdmission fencing = new(fence, new UnreachableLocationObserver(), new UnreachableCharacterProfiles());
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(), CreateCatalog(), s_metrics, fencing);

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, fence.Calls);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_AccountSessionRejected_DoesNotRotateDurableFence()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        RecordingAccountSessionStore sessions = new() { AdmitEntries = false };
        RefusingLocationFence fence = new();
        FencedCharacterAdmission fencing = new(fence, new UnreachableLocationObserver(), new UnreachableCharacterProfiles());
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(sessions), CreateCatalog(), s_metrics, fencing);

        Assert.Null(await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));
        Assert.Equal(0, fence.Calls);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulFenceUsesRehydratedProfileAndPropagatesAuthority()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile original = CreateProfile();
        CharacterLoginProfile rehydrated = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(original));
        Guid token = Guid.NewGuid();
        PersistedCharacterLocation location = new(rehydrated.Location, revision: 3, writerToken: token);
        CharacterLocationWriteAuthority authority = new(AccountId, original.Identity.CharacterId, token, location);
        FencedCharacterAdmission fencing = new(new ReturningLocationFence(authority),
            new ReturningLocationObserver(location), new ReturningCharacterProfiles(rehydrated));
        ExistingCharacterBootstrapProcessor processor = new(new CharacterPresenceDirectory(), TestAccountSessions.Create(),
            CreateCatalog(), s_metrics, fencing);

        await using ExistingCharacterLoginContext login = Assert.IsType<ExistingCharacterLoginContext>(
            await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.Same(rehydrated, login.Profile);
        Assert.Same(rehydrated, login.Connection.Profile);
        Assert.Same(authority, login.Connection.LocationAuthority);
    }

    private sealed class ReturningLocationFence(CharacterLocationWriteAuthority authority) : ICharacterLocationFenceStore
    {
        public ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CharacterLocationWriteAuthority?>(authority);
    }

    private sealed class ReturningLocationObserver(PersistedCharacterLocation location) : ICharacterLocationObservationStore
    {
        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<PersistedCharacterLocation?>(location);
    }

    private sealed class ReturningCharacterProfiles(CharacterLoginProfile profile) : ICharacterLoginProfileRepository
    {
        public ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<CharacterLoginProfile?>(profile);
    }

    private sealed class RefusingLocationFence : ICharacterLocationFenceStore
    {
        public int Calls { get; private set; }

        public ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<CharacterLocationWriteAuthority?>(null);
        }
    }

    private sealed class UnreachableLocationObserver : ICharacterLocationObservationStore
    {
        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The observer cannot run after refused acquisition.");
    }

    private sealed class UnreachableCharacterProfiles : ICharacterLoginProfileRepository
    {
        public ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A refused admission cannot rehydrate a profile.");
    }

    [Fact]
    public async Task ProcessAsync_FencedAdmissionReloadsCanonicalMapBeforeCheckingAvailability()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile rehydrated = CreateProfile();
        CharacterLoginProfile original = new(rehydrated.Identity, rehydrated.Appearance, rehydrated.Progression,
            rehydrated.Attributes, rehydrated.Vitals, rehydrated.Economy, rehydrated.PkPoints,
            rehydrated.TitleId, rehydrated.EnlightenmentPoints, new CharacterLocation(MapId + 1000, 1, 1));
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(original));
        Guid token = Guid.NewGuid();
        PersistedCharacterLocation persisted = new(rehydrated.Location, revision: 6, writerToken: token);
        CharacterLocationWriteAuthority authority = new(AccountId, original.Identity.CharacterId, token, persisted);
        FencedCharacterAdmission fencing = new(new ReturningLocationFence(authority),
            new ReturningLocationObserver(persisted), new ReturningCharacterProfiles(rehydrated));
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, MapFlags);
        ExistingCharacterBootstrapProcessor processor = new(new CharacterPresenceDirectory(), TestAccountSessions.Create(),
            WorldMapTestContent.CreateCatalog(map), s_metrics, fencing);

        await using ExistingCharacterLoginContext login = Assert.IsType<ExistingCharacterLoginContext>(
            await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.Same(rehydrated, login.Profile);
        Assert.Same(map, login.Map);
        Assert.Same(authority, login.Connection.LocationAuthority);
    }

    [Fact]
    public async Task ProcessAsync_ExistingCharacter_WritesBootstrapOrderAndStartsLoginContext()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile(rebirthCount: 2, preRebirthLevel: 130);
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, MapFlags);
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(), WorldMapTestContent.CreateCatalog(map), s_metrics);
        int receiveCallCountBeforeBootstrap = fixture.Transport.ReceiveCallCount;

        ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.Same(profile, result.Connection.Profile);
        Assert.True(presence.IsOnline(profile.Identity.CharacterId));
        Assert.Same(profile, result.Profile);
        Assert.Same(map, result.Map);
        Assert.Equal(MapId, result.Map.MapId);
        Assert.Equal(MapDataId, result.Map.MapDataId);
        Assert.Equal(MapFlags, result.Map.Flags);
        Assert.Same(map.BaseTerrain, result.Map.Collision.BaseTerrain);
        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.Equal(receiveCallCountBeforeBootstrap, fixture.Transport.ReceiveCallCount);
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());

        byte[] encryptedBootstrap = fixture.Transport.SentBytes[fixture.AuthenticationBoundary..];
        byte[] plaintextBootstrap = fixture.Client.DecryptServerBytes(encryptedBootstrap);

        Assert.Equal(
            [
                GameChatPacket.PacketIdentifier,
                GameLoginHistoryPacket.PacketIdentifier,
                GameServerStatePacket.PacketIdentifier,
                GameCharacterInfoPacket.PacketIdentifier,
            ],
            ReadPacketIds(plaintextBootstrap));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
    }

    [Fact]
    public async Task ProcessAsync_RecordsTheAccountSessionAndReleasesItWithTheConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        RecordingAccountSessionStore sessions = new();
        ExistingCharacterBootstrapProcessor processor = new(new CharacterPresenceDirectory(), TestAccountSessions.Create(sessions), CreateCatalog(), s_metrics);

        ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.Equal([(AccountId, profile.Identity.CharacterId, SessionUid)], sessions.Entered);
        Assert.Empty(sessions.Left);

        await result.DisposeAsync();

        Assert.Equal([(AccountId, SessionUid)], sessions.Left);
    }

    [Fact]
    public async Task ProcessAsync_KickArrivingWhileTheAccountIsRecorded_RevokesTheLoginAndKeepsItsRecord()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        RecordingAccountSessionStore sessions = new();
        AccountSessionKickHostedService kicks = new(sessions, presence, TimeProvider.System, new GameServerFailureState(),
            NullLogger<AccountSessionKickHostedService>.Instance);
        sessions.QueueKicks(new AccountSessionKick(AccountId, profile.Identity.CharacterId, SessionUid));
        sessions.OnEnter = () => new ValueTask(kicks.ProcessKicksAsync(TestContext.Current.CancellationToken));
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(sessions), CreateCatalog(), s_metrics);

        await using ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(
            await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.True(result.Connection.IsRevoked);
        Assert.Empty(sessions.Left);

        await result.DisposeAsync();

        Assert.Equal([(AccountId, SessionUid)], sessions.Left);
    }

    [Fact]
    public async Task ProcessAsync_AccountPlayingInAnotherRealm_RefusesWithLoginChannelTalkBeforeBootstrap()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        RecordingAccountSessionStore sessions = new() { AdmitEntries = false };
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(sessions), CreateCatalog(), s_metrics);

        ExistingCharacterLoginContext? result = await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Empty(sessions.Left);

        byte[] plaintext = fixture.Client.DecryptServerBytes(fixture.Transport.SentBytes[fixture.AuthenticationBoundary..]);

        Assert.Equal([GameChatPacket.PacketIdentifier], ReadPacketIds(plaintext));
        Assert.Equal(0x835, BinaryPrimitives.ReadUInt16LittleEndian(plaintext.AsSpan(WireFrameHeader.Size + sizeof(uint))));
        Assert.NotEqual(-1, plaintext.AsSpan().IndexOf("Your account is still online. Please try again later."u8));
    }

    [Fact]
    public async Task ProcessAsync_UnavailableWorldMap_RejectsBeforeBootstrapAndDisposesConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        WorldMapContent unavailableMap = WorldMapTestContent.Create(MapId + 1, MapDataId, MapFlags);
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(), WorldMapTestContent.CreateCatalog(unavailableMap), s_metrics);
        int receiveCallCountBeforeBootstrap = fixture.Transport.ReceiveCallCount;

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains($"unavailable world map {MapId}", exception.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(receiveCallCountBeforeBootstrap, fixture.Transport.ReceiveCallCount);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_UnavailableWorldMapAndCleanupFailure_AreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using AuthenticatedFixture fixture = await AuthenticateAsync(disposeFailure: cleanupFailure);
        CharacterLoginProfile profile = CreateProfile();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(profile));
        CharacterPresenceDirectory presence = new();
        WorldMapContent unavailableMap = WorldMapTestContent.Create(MapId + 1, MapDataId, MapFlags);
        ExistingCharacterBootstrapProcessor processor = new(presence, TestAccountSessions.Create(), WorldMapTestContent.CreateCatalog(unavailableMap), s_metrics);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidDataException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(presence.IsOnline(profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_SequentialReuseOfHandoffIsRejectedBeforeSecondBootstrapWrite()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());

        await using ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        int sentLengthAfterFirstBootstrap = fixture.Transport.SentBytes.Length;

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(sentLengthAfterFirstBootstrap, fixture.Transport.SentBytes.Length);
        Assert.Equal(0, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_OverlappingReuseOfHandoffAllowsExactlyOneOwner()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());

        fixture.Transport.BlockSends();

        Task<ExistingCharacterLoginContext?> first = processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(0, fixture.Transport.DisposeCount);
        }
        finally
        {
            fixture.Transport.ReleaseSends();
        }

        await using ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(await first);

        Assert.Equal(0, fixture.Transport.DisposeCount);

        byte[] encryptedBootstrap = fixture.Transport.SentBytes[fixture.AuthenticationBoundary..];
        byte[] plaintextBootstrap = fixture.Client.DecryptServerBytes(encryptedBootstrap);

        Assert.Equal(
            [
                GameChatPacket.PacketIdentifier,
                GameLoginHistoryPacket.PacketIdentifier,
                GameServerStatePacket.PacketIdentifier,
                GameCharacterInfoPacket.PacketIdentifier,
            ],
            ReadPacketIds(plaintextBootstrap));
    }

    [Fact]
    public async Task ProcessAsync_DisposedHandoffIsRejectedWithoutWritingBootstrap()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());

        await handoff.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessAsync_LoginContextDisposalDisposesOwnedConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        CharacterPresenceDirectory presence = new();
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(presence);

        ExistingCharacterLoginContext result = Assert.IsType<ExistingCharacterLoginContext>(await processor.ProcessAsync(handoff, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Transport.DisposeCount);
        Assert.True(presence.IsOnline(result.Profile.Identity.CharacterId));

        await result.DisposeAsync();

        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.False(presence.IsOnline(result.Profile.Identity.CharacterId));
        Assert.Throws<InvalidOperationException>(() => result.Connection);
    }

    [Fact]
    public async Task ProcessAsync_CharacterCreationRouteIsRejectedWithoutWritingBootstrapAndConnectionIsDisposed()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.CharacterCreation());
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("handoff", exception.ParamName);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperationWritesNothingAndDisposesConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(handoff, cancellation.Token).AsTask());

        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringBootstrapWriteWritesNoPartialPacketAndDisposesConnection()
    {
        await using AuthenticatedFixture fixture = await AuthenticateAsync();
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.ExistingCharacter(CreateProfile()));
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        fixture.Transport.BlockSends();

        Task<ExistingCharacterLoginContext?> processing = processor.ProcessAsync(handoff, cancellation.Token).AsTask();

        await fixture.Transport.SendBlocked.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        fixture.Transport.ReleaseSends();

        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    [Fact]
    public async Task ProcessAsync_ProcessingFailureAndCleanupFailureAreAggregated()
    {
        IOException cleanupFailure = new("transport dispose failed");
        await using AuthenticatedFixture fixture = await AuthenticateAsync(disposeFailure: cleanupFailure);
        CharacterLoginHandoffResult handoff = new(fixture.Connection, CharacterLoginResolution.CharacterCreation());
        ExistingCharacterBootstrapProcessor processor = CreateProcessor(new CharacterPresenceDirectory());

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(handoff, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<ArgumentException>(exception.InnerExceptions[0]);
        Assert.Same(cleanupFailure, exception.InnerExceptions[1]);
        Assert.Equal(fixture.AuthenticationBoundary, fixture.Transport.SentBytes.Length);
        Assert.Equal(1, fixture.Transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => handoff.TakeConnection());
    }

    private static ExistingCharacterBootstrapProcessor CreateProcessor(ICharacterPresenceRegistrar presence)
    {
        return new ExistingCharacterBootstrapProcessor(presence, TestAccountSessions.Create(), CreateCatalog(), s_metrics);
    }

    private static WorldMapContentCatalog CreateCatalog()
    {
        return WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(MapId, MapDataId, MapFlags));
    }

    private static async Task<AuthenticatedFixture> AuthenticateAsync(Exception? disposeFailure = null)
    {
        FakeGameTransportConnection transport = new(remoteEndPoint: new IPEndPoint(s_remoteAddress, 40000), disposeFailure: disposeFailure);
        FakeRedemptionStore store = new() { Result = new GameLoginTicketIdentity(AccountId, Username, SessionUid) };
        FakeAttemptLimiter limiter = new();
        GameConnectionHandoffProcessor processor = CreateHandoffProcessor(store, limiter);
        Task<GameConnectionAuthenticationResult> processing = processor.ProcessAsync(transport, TestContext.Current.CancellationToken).AsTask();

        await transport.SendCompleted.WaitAsync(TestContext.Current.CancellationToken);

        GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes);
        int authenticationBoundary = transport.SentBytes.Length;

        try
        {
            transport.QueueReceive([.. client.EncryptedKeyExchangeResponse, .. client.EncryptClientFrame(BuildLoginProof())]);

            GameConnectionAuthenticationResult authentication = await processing;
            AuthenticatedGameConnection connection = authentication.TakeConnection();

            Assert.Equal(GameConnectionAuthenticationStatus.Authenticated, authentication.Status);
            Assert.Equal(1, store.RedemptionCount);
            Assert.Equal(1, limiter.BeginCount);
            Assert.Throws<InvalidOperationException>(() => authentication.TakeConnection());

            return new AuthenticatedFixture(transport, client, connection, authenticationBoundary);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static GameConnectionHandoffProcessor CreateHandoffProcessor(FakeRedemptionStore store, FakeAttemptLimiter limiter)
    {
        GameLoginTicketRedeemer redeemer = new(store, limiter);
        GameConnectionAuthenticator authenticator = new(redeemer);
        return new GameConnectionHandoffProcessor(new GameTransportHandshakeProcessor(), authenticator);
    }

    private static CharacterLoginProfile CreateProfile(byte rebirthCount = 0, byte preRebirthLevel = 0)
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, "Bernie");
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 123456789, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount, preRebirthLevel);
        CharacterAttributes attributes = new(101, 102, 103, 104, 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(mapId: MapId, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: -25, titleId: 321, enlightenmentPoints: 1234, location);
    }

    private static byte[] BuildLoginProof()
    {
        byte[] packet = new byte[GameLoginProof.PacketLength];

        WireFrameHeader.Write(packet, GameLoginProof.PacketLength, GameLoginProof.PacketId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), SessionUid);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), AuthenticationKey);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), GameLoginProof.ExpectedMode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14), LocaleTag);
        packet[16] = 0x11;
        packet[17] = 0x22;
        packet[18] = 0x33;
        packet[19] = 0x44;
        packet[20] = 0x55;
        packet[21] = 0x66;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24), ResourceVersion);

        return packet;
    }

    private static ushort[] ReadPacketIds(ReadOnlySpan<byte> plaintext)
    {
        List<ushort> packetIds = [];
        int offset = 0;

        while (offset < plaintext.Length)
        {
            Assert.True(WireFrameHeader.TryRead(plaintext[offset..], out WireFrameHeader header));

            int wireLength = checked(header.Length + GameWireProtocol.SignatureLength);
            Assert.True(wireLength <= plaintext.Length - offset);
            Assert.True(plaintext.Slice(offset + header.Length, GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

            packetIds.Add(header.PacketId);
            offset += wireLength;
        }

        Assert.Equal(plaintext.Length, offset);
        return packetIds.ToArray();
    }

    private sealed class AuthenticatedFixture(FakeGameTransportConnection transport, GameClientTestPeer client, AuthenticatedGameConnection connection, int authenticationBoundary) : IAsyncDisposable
    {
        public FakeGameTransportConnection Transport { get; } = transport;
        public GameClientTestPeer Client { get; } = client;
        public AuthenticatedGameConnection Connection { get; } = connection;
        public int AuthenticationBoundary { get; } = authenticationBoundary;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();

            if (Transport.DisposeCount == 0)
            {
                await Connection.DisposeAsync();
            }
        }
    }
}
