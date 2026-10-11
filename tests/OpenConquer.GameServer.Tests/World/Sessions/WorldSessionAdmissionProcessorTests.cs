using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;

namespace OpenConquer.GameServer.Tests.World.Sessions;

public sealed class WorldSessionAdmissionProcessorTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;

    [Fact]
    public async Task ProcessAsync_TransfersExactConnectionAndAllBootstrapState()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0x11223344);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        ExistingCharacterBootstrapCompleteConnection bootstrap = await CreateBootstrapAsync(transport, presence, map);
        WorldSessionAdmissionProcessor processor = new(runtime);

        await using WorldSession session = await processor.ProcessAsync(bootstrap, TestContext.Current.CancellationToken);

        Assert.Same(bootstrap, session.Bootstrap);
        Assert.Same(map, session.Map);
        Assert.Same(bootstrap.Profile, session.Profile);
        Assert.Same(bootstrap.ItemSet, session.Bootstrap.ItemSet);
        Assert.Same(bootstrap.SocialRelationSet, session.Bootstrap.SocialRelationSet);
        Assert.Same(bootstrap.WeaponSkillSet, session.Bootstrap.WeaponSkillSet);
        Assert.Same(bootstrap.MagicSet, session.Bootstrap.MagicSet);
        Assert.Same(bootstrap.SyndicateState, session.Bootstrap.SyndicateState);
        Assert.Equal(0x12345678u, session.Bootstrap.ClientReportedSilentDataChecksum);
        Assert.Equal(17u, session.Bootstrap.ClientReportedSilentDataVersion);
        Assert.True(presence.IsOnline(CharacterId));
        Assert.Equal(0, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => bootstrap.TakeConnection());
        Assert.NotNull(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));

        await bootstrap.DisposeAsync();
        Assert.Equal(0, transport.DisposeCount);

        await session.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessAsync_RejectsDifferentCanonicalMapReferenceBeforeAdmitting()
    {
        WorldMapContent canonical = WorldMapTestContent.Create(MapId, MapDataId, 0);
        WorldMapContent replacement = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(canonical);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        ExistingCharacterBootstrapCompleteConnection bootstrap = await CreateBootstrapAsync(transport, presence, replacement);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => new WorldSessionAdmissionProcessor(runtime).ProcessAsync(bootstrap, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("canonical map snapshot", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Throws<InvalidOperationException>(() => bootstrap.TakeConnection());

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledOperationDisposesConnectionWithoutReservingCharacter()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        ExistingCharacterBootstrapCompleteConnection bootstrap = await CreateBootstrapAsync(transport, presence, map);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new WorldSessionAdmissionProcessor(runtime).ProcessAsync(bootstrap, cancellation.Token).AsTask());

        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        Assert.True(await runtime.RemoveCharacterAsync(ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessAsync_AlreadyRevokedConnectionIsRejectedBeforeWorldAdmission()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        ExistingCharacterBootstrapCompleteConnection bootstrap = await CreateBootstrapAsync(transport, presence, map);
        using ICharacterPresenceLease replacement = presence.Register(CharacterId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new WorldSessionAdmissionProcessor(runtime).ProcessAsync(bootstrap, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, transport.DisposeCount);
        Assert.True(presence.IsOnline(CharacterId));
        WorldCharacterOwnership admitted = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        Assert.True(await runtime.RemoveCharacterAsync(admitted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessAsync_DuplicateWorldOwnerFailsClosedAndDisposesRejectedConnection()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        WorldSessionAdmissionProcessor processor = new(runtime);
        CharacterPresenceDirectory firstPresence = new();
        CharacterPresenceDirectory secondPresence = new();
        FakeGameTransportConnection firstTransport = new();
        FakeGameTransportConnection secondTransport = new();

        await using WorldSession first = await processor.ProcessAsync(
            await CreateBootstrapAsync(firstTransport, firstPresence, map), TestContext.Current.CancellationToken);

        ExistingCharacterBootstrapCompleteConnection duplicate = await CreateBootstrapAsync(secondTransport, secondPresence, map);

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(duplicate, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, firstTransport.DisposeCount);
        Assert.Equal(1, secondTransport.DisposeCount);
        Assert.False(secondPresence.IsOnline(CharacterId));
        Assert.NotNull(await runtime.ReadCharacterAsync(first.Ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessAsync_CapacityRejectionDisposesConnectionAndPreservesCurrentSession()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map, maxCharacters: 1);
        runtime.Start();
        WorldSessionAdmissionProcessor processor = new(runtime);
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection firstTransport = new();
        FakeGameTransportConnection rejectedTransport = new();

        await using WorldSession first = await processor.ProcessAsync(
            await CreateBootstrapAsync(firstTransport, presence, map), TestContext.Current.CancellationToken);

        ExistingCharacterBootstrapCompleteConnection rejected = await CreateBootstrapAsync(rejectedTransport, presence, map, characterId: CharacterId + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(rejected, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, rejectedTransport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId + 1));
        Assert.NotNull(await runtime.ReadCharacterAsync(first.Ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveCharacterAsync_CannotBypassActiveWorldSessionOwnership()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        WorldSession session = await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(
            await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RemoveCharacterAsync(session.Ownership, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("active world session", failure.Message, StringComparison.Ordinal);
        Assert.False(session.IsStopping);
        Assert.Equal(0, transport.DisposeCount);
        Assert.True(presence.IsOnline(CharacterId));
        Assert.NotNull(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));

        await session.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revocation_AutomaticallyTerminatesSessionAndReleasesWorldResidency()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        WorldSessionAdmissionProcessor processor = new(runtime);

        WorldSession session = await processor.ProcessAsync(await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);
        using ICharacterPresenceLease replacement = presence.Register(CharacterId);

        await session.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        Assert.True(session.IsStopping);
        Assert.Equal(1, transport.DisposeCount);
        Assert.True(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(session.ThrowIfInactive);
        await session.DisposeAsync();

        WorldCharacterOwnership newOwner = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        Assert.NotEqual(session.Ownership.Generation, newOwner.Generation);
        await session.DisposeAsync();
        Assert.NotNull(await runtime.ReadCharacterAsync(newOwner, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuntimeStop_AutomaticallyTerminatesSessionAndDisconnectsTransport()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        WorldSession session = await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(
            await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);

        await runtime.StopAsync();
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeStop_WaitsForBlockedTransportDisposal()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new(blockDispose: true);
        WorldSession session = await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(
            await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);

        Task stopping = runtime.StopAsync();

        try
        {
            await transport.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(stopping.IsCompleted);
            Assert.False(session.Completion.IsCompleted);
            Assert.True(session.IsStopping);
        }
        finally
        {
            transport.ReleaseDispose();
        }

        await stopping.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeStop_PreservesFailuresFromIndependentTrackedLifetimes()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        WorldRuntime runtime = CreateRuntime(map, maxCharacters: 2);
        runtime.Start();

        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        IOException sessionFailure = new("session cleanup failed");
        InvalidOperationException admissionFailure = new("admission cleanup failed");
        Task sessionCompletion = Task.FromException(sessionFailure);
        Task admissionCompletion = Task.FromException(admissionFailure);

        runtime.TrackSession(ownership, sessionCompletion);
        runtime.TrackAdmission(admissionCompletion);

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
            () => runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

        Assert.Contains(failure.InnerExceptions, exception => ReferenceEquals(exception, sessionFailure));
        Assert.Contains(failure.InnerExceptions, exception => ReferenceEquals(exception, admissionFailure));
        Assert.False(runtime.IsRunning);

        await Assert.ThrowsAsync<AggregateException>(() => runtime.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallsDisposeTransportAndReleaseOwnerOnlyOnce()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        FakeGameTransportConnection transport = new();
        WorldSession session = await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(
            await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(
            async () => await session.DisposeAsync(), TestContext.Current.CancellationToken)));

        Assert.Equal(1, transport.DisposeCount);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
        Assert.True(session.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposeAsync_TransportFailureStillReleasesWorldOwnership()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map, maxCharacters: 1);
        runtime.Start();
        CharacterPresenceDirectory presence = new();
        IOException disposalFailure = new("transport disposal failed");
        FakeGameTransportConnection transport = new(disposeFailure: disposalFailure);
        WorldSession session = await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(
            await CreateBootstrapAsync(transport, presence, map), TestContext.Current.CancellationToken);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => session.DisposeAsync().AsTask());

        Assert.Contains(disposalFailure, exception.Flatten().InnerExceptions);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));

        WorldCharacterOwnership replacement = await runtime.AdmitCharacterAsync(CreateProfile(CharacterId), TestContext.Current.CancellationToken);
        Assert.NotEqual(session.Ownership.Generation, replacement.Generation);
        await Assert.ThrowsAsync<AggregateException>(() => session.DisposeAsync().AsTask());
    }

    private static WorldRuntime CreateRuntime(WorldMapContent map, int maxCharacters = 8)
    {
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(map);
        return new WorldRuntime(maps, new WorldExecutionOptions(2, 8, 4, maxCharacters, maxCharacters));
    }

    private static async Task<ExistingCharacterBootstrapCompleteConnection> CreateBootstrapAsync(FakeGameTransportConnection transport,
        ICharacterPresenceRegistrar presence, WorldMapContent map, uint characterId = CharacterId)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        AuthenticatedGameConnection authenticated = new(accountId: 42, username: "Bernie", sessionUid: 0x10203040,
            localeTag: 0x6E45, hardwareAddress: 0x0000665544332211, resourceVersion: 5517, session: session);
        CharacterLoginProfile profile = CreateProfile(characterId);
        ExistingCharacterGameConnection connection = new(authenticated, profile, presence.Register(characterId));

        return new ExistingCharacterBootstrapCompleteConnection(connection, map,
            new CharacterItemSet(characterId, []), new CharacterSocialRelationSet(characterId, []),
            new CharacterWeaponSkillSet(characterId, []), new CharacterMagicSet(characterId, []),
            new CharacterSyndicateState(characterId, membership: null, syndicate: null), 0x12345678, 17);
    }

    private static CharacterLoginProfile CreateProfile(uint characterId)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(MapId, 0, 0);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, 0, 0, 0, location);
    }
}
