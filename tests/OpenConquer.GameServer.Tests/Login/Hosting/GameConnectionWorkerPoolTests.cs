using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Handshake;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.Hosting;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.Tests.Realms;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.GameServer.World.Presence;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Transport.Admission;

namespace OpenConquer.GameServer.Tests.Login.Hosting;

public sealed class GameConnectionWorkerPoolTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;

    [Fact]
    public async Task RunAsync_WaitsForSessionCleanupAndRecordsFatalFailureBeforeCompleting()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();

        IOException disposalFailure = new("owned transport disposal failed");
        FakeGameTransportConnection transport = new(blockDispose: true, disposeFailure: disposalFailure);
        CharacterPresenceDirectory presence = new();
        WorldSession session = await CreateSessionAsync(runtime, transport, presence, map);

        transport.QueueEndOfStream();

        await using TransportConnectionAdmissionQueue queue = new(8);
        using GameLoginMetrics metrics = new();
        using RecordingApplicationLifetime lifetime = new();
        GameServerFailureState failureState = new();

        GameConnectionWorkerPool workers = new(queue,
            (_, _) => ValueTask.FromResult<WorldSession?>(session),
            CreateOptions(), metrics, failureState, lifetime, NullLogger<GameConnectionWorkerPool>.Instance);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(transport));
        queue.Complete();

        Task running = workers.RunAsync(TestContext.Current.CancellationToken);

        try
        {
            await transport.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(running.IsCompleted);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(failureState.Failure);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);

            transport.ReleaseDispose();

            await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Exception failure = Assert.IsAssignableFrom<Exception>(failureState.Failure);

            Assert.True(ContainsException(failure, disposalFailure));
            Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(presence.IsOnline(CharacterId));
            Assert.Equal(1, transport.DisposeCount);
            Assert.True(session.Completion.IsFaulted);
        }
        finally
        {
            transport.ReleaseDispose();
        }
    }

    [Fact]
    public async Task RunAsync_CleanSecuredPeerDisconnectReleasesSessionWithoutFatalShutdown()
    {
        WorldMapContent map = WorldMapTestContent.Create(MapId, MapDataId, 0);
        await using WorldRuntime runtime = CreateRuntime(map);
        runtime.Start();

        FakeGameTransportConnection transport = new();
        CharacterPresenceDirectory presence = new();
        WorldSession session = await CreateSessionAsync(runtime, transport, presence, map);

        transport.QueueEndOfStream();

        await using TransportConnectionAdmissionQueue queue = new(8);
        using GameLoginMetrics metrics = new();
        using RecordingApplicationLifetime lifetime = new();
        GameServerFailureState failureState = new();

        GameConnectionWorkerPool workers = new(queue,
            (_, _) => ValueTask.FromResult<WorldSession?>(session),
            CreateOptions(), metrics, failureState, lifetime, NullLogger<GameConnectionWorkerPool>.Instance);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(transport));
        queue.Complete();

        await workers.RunAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        Assert.Null(failureState.Failure);
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.Equal(1, transport.DisposeCount);
        Assert.True(session.Completion.IsCompletedSuccessfully);
        Assert.False(presence.IsOnline(CharacterId));
        Assert.Null(await runtime.ReadCharacterAsync(session.Ownership, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_NotAdmittedConnectionDoesNotCreateWorldSessionOrRequestFatalShutdown()
    {
        FakeGameTransportConnection transport = new();

        await using TransportConnectionAdmissionQueue queue = new(8);
        using GameLoginMetrics metrics = new();
        using RecordingApplicationLifetime lifetime = new();
        GameServerFailureState failureState = new();

        int processed = 0;

        GameConnectionWorkerPool workers = new(queue,
            async (connection, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref processed);
                await connection.DisposeAsync();
                return null;
            },
            CreateOptions(), metrics, failureState, lifetime, NullLogger<GameConnectionWorkerPool>.Instance);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(transport));
        queue.Complete();

        await workers.RunAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Null(failureState.Failure);
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
    }

    private static GameNetworkOptions CreateOptions()
    {
        return new GameNetworkOptions(new IPEndPoint(IPAddress.Loopback, 5816),
            listenBacklog: 8, admissionCapacity: 8, maximumConnectionsPerSource: 2,
            workerCount: 1, connectionTimeout: TimeSpan.FromSeconds(30));
    }

    private static WorldRuntime CreateRuntime(WorldMapContent map)
    {
        return new WorldRuntime(WorldMapTestContent.CreateCatalog(map),
            new WorldExecutionOptions(2, 8, 4, 8, 8));
    }

    private static async Task<WorldSession> CreateSessionAsync(WorldRuntime runtime, FakeGameTransportConnection transport,
        ICharacterPresenceRegistrar presence, WorldMapContent map)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        GameConnectionSession connectionSession = await GameConnectionSession.OpenAsync(transport, cancellationToken);

        try
        {
            GameTransportHandshakeProcessor handshakeProcessor = new();
            Task<bool> handshake = handshakeProcessor.TryCompleteAsync(connectionSession, cancellationToken).AsTask();

            await transport.SendCompleted.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);

            using (GameClientTestPeer client = GameClientTestPeer.Create(transport.SentBytes))
            {
                transport.QueueReceive(client.EncryptedKeyExchangeResponse);

                Assert.True(await handshake.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken));
            }

            AuthenticatedGameConnection authenticated = new(accountId: 42, username: "Bernie", sessionUid: 0x10203040,
                localeTag: 0x6E45, hardwareAddress: 0x0000665544332211, resourceVersion: 5517, session: connectionSession);

            CharacterLoginProfile profile = CreateProfile();
            ExistingCharacterGameConnection character = new(authenticated, profile, presence.Register(CharacterId),
                TestAccountSessions.CreateLease(authenticated.AccountId, authenticated.SessionUid));

            ExistingCharacterBootstrapCompleteConnection bootstrap = new(character, map,
                new CharacterItemSet(CharacterId, []),
                new CharacterSocialRelationSet(CharacterId, []),
                new CharacterWeaponSkillSet(CharacterId, []),
                new CharacterMagicSet(CharacterId, []),
                new CharacterSyndicateState(CharacterId, membership: null, syndicate: null),
                0x12345678, 17);

            return await new WorldSessionAdmissionProcessor(runtime).ProcessAsync(bootstrap, cancellationToken);
        }
        catch
        {
            await connectionSession.DisposeAsync();
            throw;
        }
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(MapId, 0, 0);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, 0, 0, 0, location);
    }

    private static bool ContainsException(Exception candidate, Exception expected)
    {
        if (ReferenceEquals(candidate, expected))
        {
            return true;
        }

        if (candidate is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(exception => ContainsException(exception, expected));
        }

        return candidate.InnerException is not null && ContainsException(candidate.InnerException, expected);
    }

    private sealed class RecordingApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
        public void Dispose() => _stopping.Dispose();
    }
}
