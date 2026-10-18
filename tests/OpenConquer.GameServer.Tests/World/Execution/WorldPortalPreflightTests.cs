using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldPortalPreflightTests
{
    private const uint SourceMapId = 1002;
    private const uint TargetMapId = 1011;
    private const uint PlayerId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private static readonly MapTerrainExit s_exit = new(2, 1, 7);

    [Fact]
    public async Task AdjacentNativeExit_ReservesCatalogDestinationAndFencesMovement()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(s_exit, Resolved());
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);
            using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
                await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));

            Assert.True(reservation.IsActive);
            Assert.Equal(TargetMapId, reservation.DestinationMapId);
            Assert.Equal((ushort)1, reservation.X);
            Assert.Equal((ushort)1, reservation.Y);
            Assert.Equal(owner, reservation.SourceSnapshot.Ownership);
            Assert.Null(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
            Assert.Equal(1, runtime.ResidentCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.AdmitCharacterAsync(
                Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken).AsTask());

            reservation.Dispose();
            WorldCharacterOwnership newcomer = await runtime.AdmitCharacterAsync(
                Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.NotNull(await runtime.ReadCharacterAsync(newcomer, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task MissingAndDistantExitTiles_DoNotReserveOrFreezeOwner()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(
            new MapTerrainExit(4, 4, 7), Resolved());
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);

            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));
            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 4, 4, TestContext.Current.CancellationToken));
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
            WorldCharacterOwnership target = await runtime.AdmitCharacterAsync(
                Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.Equal(TargetMapId, target.MapId);
        }
    }

    [Fact]
    public async Task UnresolvedSourcePassway_RefusesWithoutInventingFallback()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(s_exit,
            new MapTransitionEntry(SourceMapId, 7, "no-source", null));
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);
            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));
            Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task StaleGenerationAndDeadOwner_CannotReservePortal()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(s_exit, Resolved());
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(life: 0), TestContext.Current.CancellationToken);
            WorldCharacterOwnership stale = owner with { Generation = owner.Generation + 1 };
            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, stale, 2, 1, TestContext.Current.CancellationToken));
            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));
            WorldCharacterOwnership target = await runtime.AdmitCharacterAsync(
                Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.NotNull(await runtime.ReadCharacterAsync(target, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task PendingMovementBeforeBarrier_UsesFinalServerTileAndRefundsReservation()
    {
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(s_exit, Resolved(), queueCapacity: 8);
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);
            using ManualResetEventSlim entered = new(false);
            using ManualResetEventSlim release = new(false);
            ValueTask<bool> blocker = runtime.TryPublishAvatarActionAsync(owner, _ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException("Controlled source worker was not released.");
                }
            }, TestContext.Current.CancellationToken);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
                ValueTask<WorldCharacterSnapshot?> first = runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken);
                ValueTask<WorldCharacterSnapshot?> second = runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken);
                ValueTask<WorldCharacterSnapshot?> third = runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken);
                Assert.False(third.IsCompleted);
                Task<WorldTransferReservation?> pending = WorldPortalPreflight.TryReserveAsync(
                    runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken).AsTask();
                Assert.False(pending.IsCompleted);
                release.Set();
                Assert.True(await blocker);
                Assert.NotNull(await first);
                Assert.NotNull(await second);
                Assert.Equal((ushort)4, Assert.IsType<WorldCharacterSnapshot>(await third).X);
                Assert.Null(await pending);
                WorldCharacterOwnership target = await runtime.AdmitCharacterAsync(
                    Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
                Assert.Equal(TargetMapId, target.MapId);
                Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 4, TestContext.Current.CancellationToken));
            }
            finally
            {
                release.Set();
            }
        }
    }

    [Fact]
    public async Task DuplicateExitTile_IsAmbiguousAndDenied()
    {
        MapTerrainExit second = new(s_exit.X, s_exit.Y, 8);
        (WorldRuntime runtime, MapTransitionCatalog transitions) = CreateWorld(
            [s_exit, second], [Resolved(), new MapTransitionEntry(SourceMapId, 8, "no-source", null)]);
        await using (runtime)
        {
            runtime.Start();
            WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);
            Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));
            WorldCharacterOwnership target = await runtime.AdmitCharacterAsync(
                Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
            Assert.Equal(TargetMapId, target.MapId);
        }
    }

    [Fact]
    public async Task SharedTerrain_UsesSourceWorldIdentityInsteadOfBorrowingAnotherMapDestination()
    {
        MapBaseTerrain terrain = new(1015, 5, 5,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 25), [s_exit]);
        MapTerrainCollision collision = new(terrain, []);
        WorldMapContent original = new(new MapDefinition(SourceMapId, 1015, 0), collision);
        const uint siblingMapId = 1003;
        WorldMapContent sibling = new(new MapDefinition(siblingMapId, 1015, 0), collision);
        WorldMapContent target = CreateMap(TargetMapId, 1016, []);
        WorldMapContentCatalog maps = new([original, sibling, target]);
        MapTransitionCatalog transitions = new(
            [Resolved(), new MapTransitionEntry(siblingMapId, 7, "shared-terrain-unresolved", null)], maps);
        await using WorldRuntime runtime = new(maps, new WorldExecutionOptions(1, 8, 1, 2, 1));
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(
            Profile(mapId: siblingMapId), TestContext.Current.CancellationToken);

        Assert.Null(await WorldPortalPreflight.TryReserveAsync(
            runtime, transitions, owner, 2, 1, TestContext.Current.CancellationToken));
        WorldCharacterOwnership newcomer = await runtime.AdmitCharacterAsync(
            Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
        Assert.Equal(TargetMapId, newcomer.MapId);
    }

    [Fact]
    public async Task CatalogFromDifferentWorldSnapshot_CannotReserveWrongDestination()
    {
        WorldMapContentCatalog actualMaps = new(
            [CreateMap(SourceMapId, 1015, [s_exit]), CreateMap(TargetMapId, 1016, [])]);
        WorldMapContentCatalog staleMaps = new(
            [CreateMap(SourceMapId, 1015, [s_exit]), CreateMap(TargetMapId, 1016, [])]);
        MapTransitionCatalog staleTransitions = new(
            [new MapTransitionEntry(SourceMapId, 7, "resolved",
                new MapTransitionDestination(TargetMapId, 1, 3, 3, "L"))], staleMaps);
        await using WorldRuntime runtime = new(actualMaps, new WorldExecutionOptions(1, 8, 1, 2, 1));
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(Profile(), TestContext.Current.CancellationToken);

        // The IDs and exit markers agree, but the catalog was validated against different content than the runtime
        // uses, so no route may be reserved from it.
        Assert.Null(await WorldPortalPreflight.TryReserveAsync(runtime, staleTransitions, owner, 2, 1,
            TestContext.Current.CancellationToken));
        Assert.NotNull(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
        WorldCharacterOwnership newcomer = await runtime.AdmitCharacterAsync(
            Profile(PlayerId + 1, TargetMapId), TestContext.Current.CancellationToken);
        Assert.Equal(TargetMapId, newcomer.MapId);
    }

    private static MapTransitionEntry Resolved() => new(SourceMapId, 7, "resolved",
        new MapTransitionDestination(TargetMapId, 1, 1, 1, "L"));

    private static (WorldRuntime Runtime, MapTransitionCatalog Transitions) CreateWorld(
        MapTerrainExit exit, MapTransitionEntry entry, int queueCapacity = 8) => CreateWorld([exit], [entry], queueCapacity);

    private static (WorldRuntime Runtime, MapTransitionCatalog Transitions) CreateWorld(
        MapTerrainExit[] exits, MapTransitionEntry[] entries, int queueCapacity = 8)
    {
        WorldMapContentCatalog maps = new([CreateMap(SourceMapId, 1015, exits), CreateMap(TargetMapId, 1016, [])]);
        MapTransitionCatalog catalog = new(entries, maps);
        WorldRuntime runtime = new(maps, new WorldExecutionOptions(1, queueCapacity, 1, 2, 1));
        return (runtime, catalog);
    }

    private static WorldMapContent CreateMap(uint mapId, uint mapDataId, MapTerrainExit[] exits)
    {
        MapBaseTerrain terrain = new(mapDataId, 5, 5,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 25), exits);
        return new WorldMapContent(new MapDefinition(mapId, mapDataId, 0), new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile Profile(uint characterId = PlayerId, uint mapId = SourceMapId, ushort life = 100)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "PortalTest");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(life, 0);
        CharacterEconomy economy = new(0, 0, 0);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy,
            0, 0, 0, new CharacterLocation(mapId, 1, 1));
    }
}
