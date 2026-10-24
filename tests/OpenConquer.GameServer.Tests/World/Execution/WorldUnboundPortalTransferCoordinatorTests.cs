using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldUnboundPortalTransferCoordinatorTests
{
    private const uint SourceMap = 1002;
    private const uint DestinationMap = 1011;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint AccountId = 42;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompleteUnboundPortal_PersistsVerifiedOperationAndMovesWorldGeneration()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            PersistedCharacterLocation persisted = Initial();
            int observations = 0;
            Observer reads = new((account, character, _) =>
            {
                Assert.Equal(AccountId, account);
                Assert.Equal(CharacterId, character);
                Interlocked.Increment(ref observations);
                return ValueTask.FromResult<PersistedCharacterLocation?>(persisted);
            });
            Writer writes = new(async (change, _) =>
            {
                Assert.Equal((SourceMap, (ushort)4, (ushort)3),
                    (change.Expected.MapId, change.Expected.X, change.Expected.Y));
                Assert.Equal(9, change.ExpectedRevision);
                Assert.Equal(DestinationMap, change.Destination.MapId);
                Assert.NotEqual(Guid.Empty, change.OperationId);
                Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, Token));
                persisted = new(change.Destination, change.ExpectedRevision + 1, change.OperationId, change.WriterToken);
                return CharacterLocationWriteResult.Committed;
            });

            WorldCharacterOwnership target = Assert.IsType<WorldCharacterOwnership>(await Execute(runtime, catalog, profile, source, reads, writes, TestContext.Current.CancellationToken));
            Assert.Equal(2, observations);
            Assert.Equal(1, writes.Calls);
            Assert.Equal(DestinationMap, target.MapId);
            Assert.NotEqual(source.Generation, target.Generation);
            Assert.Equal(1, runtime.ResidentCount);
            Assert.Null(await runtime.ReadCharacterAsync(source, Token));
            Assert.Equal((ushort)1, Assert.IsType<WorldCharacterSnapshot>(
                await runtime.ReadCharacterAsync(target, Token)).X);
            Assert.True(await runtime.RemoveCharacterAsync(target, Token));
            WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1), Token);
            Assert.NotEqual(0UL, other.Generation);
        }
    }

    [Fact]
    public async Task ActiveSession_IsRefusedBeforeDurableWriteAndRefundsReservation()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            TaskCompletionSource lifetime = new(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.TrackSession(source, lifetime.Task);
            Observer reads = new((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(Initial()));
            Writer writes = new((_, _) => throw new InvalidOperationException("Must not write"));
            Assert.Null(await Execute(runtime, catalog, profile, source, reads, writes, TestContext.Current.CancellationToken));
            Assert.Equal(0, writes.Calls);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(source, 0, Token));
            runtime.UntrackSession(source, lifetime.Task);
            lifetime.TrySetResult();
            Assert.True(await runtime.RemoveCharacterAsync(source, Token));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictOrUnknownWrite_FailsClosedAndStopsWorld(bool conflict)
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            Observer reads = new((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(Initial()));
            Writer writes = conflict
                ? new((_, _) => ValueTask.FromResult(CharacterLocationWriteResult.Conflict))
                : new((_, _) => throw new IOException("DB outcome unknown"));
            await Assert.ThrowsAnyAsync<Exception>(() => Execute(runtime, catalog, profile, source, reads, writes, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(1, writes.Calls);
            Assert.False(runtime.IsRunning);
            Assert.Equal(0, runtime.ResidentCount);
        }
    }

    [Fact]
    public async Task MissingExactEvidence_AfterSuccessfulWrite_IsTerminal()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            int reads = 0;
            Observer observer = new((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(
                Interlocked.Increment(ref reads) == 1 ? Initial() : new PersistedCharacterLocation(
                    new CharacterLocation(DestinationMap, 1, 1), 10, Guid.NewGuid())));
            Writer writer = new((_, _) => ValueTask.FromResult(CharacterLocationWriteResult.Committed));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(runtime, catalog, profile, source, observer, writer, TestContext.Current.CancellationToken).AsTask());
            Assert.Equal(2, reads);
            Assert.False(runtime.IsRunning);
        }
    }

    [Fact]
    public async Task PendingWrite_SealsSessionRemovalAndDuplicatePortalUntilCompletion()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            PersistedCharacterLocation persisted = Initial();
            Observer observer = new((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(persisted));
            TaskCompletionSource<CharacterLocationWriteResult> complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Writer writer = new((change, _) =>
            {
                entered.TrySetResult();
                persisted = new(change.Destination, change.ExpectedRevision + 1, change.OperationId, change.WriterToken);
                return new ValueTask<CharacterLocationWriteResult>(complete.Task);
            });
            Task<WorldCharacterOwnership?> pending = Execute(runtime, catalog, profile, source, observer, writer, TestContext.Current.CancellationToken).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
                Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, Token));
                Assert.False(await runtime.RemoveCharacterAsync(source, Token));
                Assert.Throws<InvalidOperationException>(() => runtime.TrackSession(source, Task.CompletedTask));
                Assert.Null(await Execute(runtime, catalog, profile, source, observer, writer, TestContext.Current.CancellationToken));
                Assert.Equal(1, writer.Calls);
                complete.SetResult(CharacterLocationWriteResult.Committed);
                WorldCharacterOwnership owner = Assert.IsType<WorldCharacterOwnership>(await pending);
                Assert.Equal(DestinationMap, owner.MapId);
                Assert.NotNull(await runtime.ReadCharacterAsync(owner, Token));
            }
            finally
            {
                complete.TrySetResult(CharacterLocationWriteResult.Conflict);
            }
        }
    }

    [Fact]
    public async Task CancelledBeforePreparation_CannotDispatchWrite()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            Writer writer = new((_, _) => throw new InvalidOperationException("Must not write"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Execute(runtime, catalog, profile, source,
                    new Observer((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(Initial())),
                    writer, cancelled.Token).AsTask());
            Assert.Equal(0, writer.Calls);
            Assert.True(runtime.IsRunning);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(source, 0, Token));
        }
    }

    [Fact]
    public async Task CallerCancellationDuringUncooperativeWrite_StopsWorldWithoutReleasingSource()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            using CancellationTokenSource cancelled = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<CharacterLocationWriteResult> blocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            Writer writer = new((_, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<CharacterLocationWriteResult>(blocked.Task);
            });
            Task<WorldCharacterOwnership?> pending = Execute(runtime, catalog, profile, source,
                new Observer((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(Initial())),
                writer, cancelled.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
                Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, Token));
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.False(runtime.IsRunning);
                Assert.Equal(0, runtime.ResidentCount);
                Assert.Equal(1, writer.Calls);
            }
            finally
            {
                cancelled.Cancel();
                blocked.TrySetResult(CharacterLocationWriteResult.Conflict);
            }
        }
    }

    [Fact]
    public async Task CallerCancellationDuringUncooperativeEvidenceRead_StopsWorld()
    {
        (WorldRuntime runtime, MapTransitionCatalog catalog) = World();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(profile, Token);
            using CancellationTokenSource cancelled = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<PersistedCharacterLocation?> blocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            int observations = 0;
            Observer observer = new((_, _, _) =>
            {
                if (Interlocked.Increment(ref observations) == 1)
                {
                    return ValueTask.FromResult<PersistedCharacterLocation?>(Initial());
                }
                entered.TrySetResult();
                return new ValueTask<PersistedCharacterLocation?>(blocked.Task);
            });
            Writer writer = new((_, _) => ValueTask.FromResult(CharacterLocationWriteResult.Committed));
            Task<WorldCharacterOwnership?> pending = Execute(runtime, catalog, profile, source,
                observer, writer, cancelled.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
                Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, Token));
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.False(runtime.IsRunning);
                Assert.Equal(0, runtime.ResidentCount);
                Assert.Equal(2, observations);
                Assert.Equal(1, writer.Calls);
            }
            finally
            {
                cancelled.Cancel();
                blocked.TrySetResult(null);
            }
        }
    }

    private static ValueTask<WorldCharacterOwnership?> Execute(WorldRuntime runtime, MapTransitionCatalog catalog,
        CharacterLoginProfile profile, WorldCharacterOwnership owner, ICharacterLocationObservationStore observations,
        ICharacterLocationWriteStore writer, CancellationToken token = default,
        ICharacterLocationFenceStore? fence = null) =>
        WorldUnboundPortalTransferCoordinator.TryExecuteAsync(runtime, catalog, profile, owner,
            observations, writer, fence ?? new Fence(), portalX: 2, portalY: 1, cancellationToken: token);

    private sealed class Fence : ICharacterLocationFenceStore
    {
        public ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId,
            CancellationToken cancellationToken = default)
        {
            Guid token = Guid.NewGuid();
            PersistedCharacterLocation snapshot = new(new CharacterLocation(SourceMap, 4, 3), 9, writerToken: token);
            return ValueTask.FromResult<CharacterLocationWriteAuthority?>(
                new CharacterLocationWriteAuthority(accountId, characterId, token, snapshot));
        }
    }

    private static PersistedCharacterLocation Initial() => new(new CharacterLocation(SourceMap, 4, 3), 9);

    private sealed class Observer(Func<uint, uint, CancellationToken, ValueTask<PersistedCharacterLocation?>> observe)
        : ICharacterLocationObservationStore
    {
        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
            CancellationToken cancellationToken = default) => observe(accountId, characterId, cancellationToken);
    }

    private sealed class Writer(Func<CharacterLocationChange, CancellationToken, ValueTask<CharacterLocationWriteResult>> commit)
        : ICharacterLocationWriteStore
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        public ValueTask<CharacterLocationWriteResult> TryCommitAsync(CharacterLocationChange change,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return commit(change, cancellationToken);
        }
    }

    private static (WorldRuntime Runtime, MapTransitionCatalog Catalog) World()
    {
        MapTerrainExit exit = new(2, 1, 7);
        WorldMapContentCatalog maps = new([Map(SourceMap, 1015, [exit]), Map(DestinationMap, 1016, [])]);
        MapTransitionCatalog catalog = new(
            [new MapTransitionEntry(SourceMap, 7, "release-approved",
                new MapTransitionDestination(DestinationMap, 1, 1, 1, "L"))], maps);
        return (new WorldRuntime(maps, new WorldExecutionOptions(1, 8, 1, 3, 1)), catalog);
    }

    private static WorldMapContent Map(uint mapId, uint terrainId, MapTerrainExit[] exits)
    {
        MapBaseTerrain terrain = new(terrainId, 5, 5,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 25), exits);
        return new WorldMapContent(new MapDefinition(mapId, terrainId, 0), new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile Profile(uint characterId = CharacterId)
    {
        CharacterLoginIdentity identity = new(characterId, AccountId, "PortalTransfer");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy,
            0, 0, 0, new CharacterLocation(SourceMap, 1, 1));
    }
}
