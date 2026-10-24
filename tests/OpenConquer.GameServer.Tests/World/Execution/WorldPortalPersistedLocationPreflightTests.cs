using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldPortalPersistedLocationPreflightTests
{
    private const uint SourceMapId = 1002;
    private const uint TargetMapId = 1011;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint AccountId = 42;

    [Fact]
    public async Task SuccessfulPreparation_PairsPersistedLocationWithSourceGenerationAndRefundsOnDispose()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
            ObservationStore observations = new((account, character, _) =>
            {
                Assert.Equal(AccountId, account);
                Assert.Equal(CharacterId, character);
                return ValueTask.FromResult<PersistedCharacterLocation?>(new PersistedCharacterLocation(new CharacterLocation(SourceMapId, 4, 3), 9));
            });

            WorldPortalPersistedLocationReservation prepared = Assert.IsType<WorldPortalPersistedLocationReservation>(
                await WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions, profile, owner,
                    observations, 2, 1, TestContext.Current.CancellationToken));
            using (prepared)
            {
                Assert.Equal(1, observations.Calls);
                Assert.True(prepared.IsActive);
                Assert.Equal(owner, prepared.SourceOwnership);
                Assert.Equal(owner, prepared.SourceSnapshot.Ownership);
                Assert.Equal((ushort)1, prepared.SourceSnapshot.X);
                Assert.Equal((ushort)1, prepared.SourceSnapshot.Y);
                Assert.Equal((SourceMapId, (ushort)4, (ushort)3),
                    (prepared.PersistedSource.Location.MapId, prepared.PersistedSource.Location.X, prepared.PersistedSource.Location.Y));
                Assert.Equal(9, prepared.PersistedSource.Revision);
                Assert.Equal(TargetMapId, prepared.DestinationMapId);
                Assert.Equal((ushort)1, prepared.DestinationX);
                Assert.Equal((ushort)1, prepared.DestinationY);
                Assert.Null(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
                await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.AdmitCharacterAsync(
                    Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken).AsTask());
            }

            prepared.Dispose();
            Assert.False(prepared.IsActive);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
            WorldCharacterOwnership admitted = await runtime.AdmitCharacterAsync(
                Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.NotNull(await runtime.ReadCharacterAsync(admitted, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task MismatchedLoginCharacterOrMap_IsRefusedBeforeObservation()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);
            ObservationStore observations = new((_, _, _) => throw new InvalidOperationException("Must not observe"));

            Assert.Null(await WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions,
                Profile(CharacterId + 1), owner, observations, 2, 1, TestContext.Current.CancellationToken));
            Assert.Null(await WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions,
                Profile(mapId: TargetMapId), owner, observations, 2, 1, TestContext.Current.CancellationToken));
            Assert.Equal(0, observations.Calls);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrOtherMapPersistence_RefundsLeaseWithoutInventingExpectedLocation(bool differentMap)
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
            ObservationStore observations = new((_, _, _) => ValueTask.FromResult<PersistedCharacterLocation?>(
                differentMap ? new PersistedCharacterLocation(new CharacterLocation(TargetMapId, 1, 1), 0) : null));

            Assert.Null(await WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions,
                profile, owner, observations, 2, 1, TestContext.Current.CancellationToken));
            Assert.Equal(1, observations.Calls);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
            WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(
                Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.NotNull(await runtime.ReadCharacterAsync(other, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ObservationFailure_PropagatesAndRefundsLease()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
            IOException failure = new("read unavailable");
            ObservationStore observations = new((_, _, _) => throw failure);

            IOException actual = await Assert.ThrowsAsync<IOException>(() =>
                WorldPortalPersistedLocationPreflight.TryReserveAsync(runtime, transitions, profile, owner,
                    observations, 2, 1, TestContext.Current.CancellationToken).AsTask());
            Assert.Same(failure, actual);
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
            WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(
                Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.NotNull(await runtime.ReadCharacterAsync(other, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task CancellationDuringUncooperativeObservation_ImmediatelyRefundsReservation()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<PersistedCharacterLocation?> blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ObservationStore observations = new((_, _, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<PersistedCharacterLocation?>(blocked.Task); // Deliberately ignores cancellation.
            });
            using CancellationTokenSource cancelled = new();
            Task<WorldPortalPersistedLocationReservation?> pending = WorldPortalPersistedLocationPreflight.TryReserveAsync(
                runtime, transitions, profile, owner, observations, 2, 1, cancelled.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
                Assert.Null(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
                WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(
                    Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
                Assert.NotNull(await runtime.ReadCharacterAsync(other, TestContext.Current.CancellationToken));
            }
            finally
            {
                cancelled.Cancel();
                blocked.TrySetResult(new PersistedCharacterLocation(new CharacterLocation(SourceMapId, 1, 1), 0));
            }
        }
    }

    [Fact]
    public async Task SourceRemovalDuringObservation_RejectsRevokedGenerationAndRefundsCapacity()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld();
        await using (runtime)
        {
            runtime.Start();
            CharacterLoginProfile profile = Profile();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(profile, TestContext.Current.CancellationToken);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<PersistedCharacterLocation?> blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ObservationStore observations = new((_, _, _) =>
            {
                entered.TrySetResult();
                return new ValueTask<PersistedCharacterLocation?>(blocked.Task);
            });

            Task<WorldPortalPersistedLocationReservation?> pending = WorldPortalPersistedLocationPreflight.TryReserveAsync(
                runtime, transitions, profile, owner, observations, 2, 1, TestContext.Current.CancellationToken).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
                Assert.True(await runtime.RemoveCharacterAsync(owner, TestContext.Current.CancellationToken));
                blocked.TrySetResult(new PersistedCharacterLocation(new CharacterLocation(SourceMapId, 1, 1), 0));
                Assert.Null(await pending);
                Assert.Null(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken));
                WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(
                    Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
                Assert.NotNull(await runtime.ReadCharacterAsync(other, TestContext.Current.CancellationToken));
            }
            finally
            {
                blocked.TrySetResult(new PersistedCharacterLocation(new CharacterLocation(SourceMapId, 1, 1), 0));
            }
        }
    }

    private sealed class ObservationStore(Func<uint, uint, CancellationToken, ValueTask<PersistedCharacterLocation?>> observe)
        : ICharacterLocationObservationStore
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return observe(accountId, characterId, cancellationToken);
        }
    }

    private static (WorldRuntime Runtime, MapTransitionCatalog Transitions) CreateWorld()
    {
        MapTerrainExit exit = new(2, 1, 7);
        WorldMapContentCatalog maps = new([CreateMap(SourceMapId, 1015, [exit]), CreateMap(TargetMapId, 1016, [])]);
        MapTransitionCatalog transitions = new(
            [new MapTransitionEntry(SourceMapId, 7, "resolved",
                new MapTransitionDestination(TargetMapId, 1, 1, 1, "L"))], maps);
        return (new WorldRuntime(maps, new WorldExecutionOptions(1, 8, 1, 2, 1)), transitions);
    }

    private static WorldMapContent CreateMap(uint mapId, uint terrainId, MapTerrainExit[] exits)
    {
        MapBaseTerrain terrain = new(terrainId, 5, 5,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 25), exits);
        return new WorldMapContent(new MapDefinition(mapId, terrainId, 0), new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile Profile(uint characterId = CharacterId, uint mapId = SourceMapId)
    {
        CharacterLoginIdentity identity = new(characterId, AccountId, "PortalTest");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy,
            0, 0, 0, new CharacterLocation(mapId, 1, 1));
    }
}
