using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldReservedResidencyHandoffTests
{
    private const uint SourceMapId = 1002;
    private const uint TargetMapId = 1011;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handoff_RebindsOnlyWorldOwnershipAndKeepsCapacityAccountedOnce()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));

        WorldCharacterOwnership destination = Assert.IsType<WorldCharacterOwnership>(
            await runtime.TryRelocateReservedCharacterAsync(reservation, CancellationToken));
        Assert.Equal(1, runtime.ResidentCount);
        Assert.NotEqual(source, destination);
        Assert.Equal(source.CharacterId, destination.CharacterId);
        Assert.True(destination.Generation > source.Generation);
        Assert.Equal(TargetMapId, destination.MapId);
        Assert.Equal(source.RuntimeId, destination.RuntimeId);
        Assert.False(reservation.IsActive);
        Assert.Null(await runtime.ReadCharacterAsync(source, CancellationToken));
        Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, CancellationToken));
        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.ReadCharacterAsync(destination, CancellationToken));
        Assert.Equal((ushort)1, snapshot.X);
        Assert.Equal((ushort)1, snapshot.Y);
        Assert.True(snapshot.IsAlive);
        Assert.NotNull(await runtime.TryWalkCharacterAsync(destination, 0, CancellationToken));

        // The source slot is free; the destination's reserved slot became active residency.
        WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, SourceMapId), CancellationToken);
        Assert.Equal(2, runtime.ResidentCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.AdmitCharacterAsync(Profile(CharacterId + 2, TargetMapId), CancellationToken).AsTask());
        reservation.Dispose();
        Assert.True(await runtime.RemoveCharacterAsync(other, CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(destination, CancellationToken));
        Assert.Equal(0, runtime.ResidentCount);
    }

    [Fact]
    public async Task Handoff_RetainsGhostStatusAndOriginalDeathTimestamp()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        WorldCharacterSnapshot ghost = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.TryEnterGhostAsync(source, System.Diagnostics.Stopwatch.GetTimestamp(), cancellationToken: CancellationToken));
        Assert.True(ghost.IsGhost);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));

        WorldCharacterOwnership destination = Assert.IsType<WorldCharacterOwnership>(
            await runtime.TryRelocateReservedCharacterAsync(reservation, CancellationToken));
        WorldCharacterSnapshot after = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.ReadCharacterAsync(destination, CancellationToken));
        Assert.False(after.IsAlive);
        Assert.Equal(ghost.Status, after.Status);
        Assert.Equal(ghost.DeadSinceTimestamp, after.DeadSinceTimestamp);
        Assert.True(after.IsGhost);
    }

    [Fact]
    public async Task Handoff_RejectsActiveSessionAndLeavesItsReservationReversible()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.TrackSession(source, completion.Task);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));

        Assert.Null(await runtime.TryRelocateReservedCharacterAsync(reservation, CancellationToken));
        Assert.True(reservation.IsActive);
        Assert.NotNull(await runtime.ReadCharacterAsync(source, CancellationToken));
        reservation.Dispose();
        Assert.NotNull(await runtime.TryWalkCharacterAsync(source, 0, CancellationToken));
        runtime.UntrackSession(source, completion.Task);
        completion.TrySetResult();
        Assert.True(await runtime.RemoveCharacterAsync(source, CancellationToken));
    }

    [Fact]
    public async Task DisposingReservationDuringStagedArrival_DoesNotUnfenceSource()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 2);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), CancellationToken);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        ValueTask<bool> blocker = runtime.TryPublishAvatarActionAsync(other, _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The target worker was never released.");
            }
        }, CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), CancellationToken));
            Task<WorldCharacterOwnership?> pending = runtime.TryRelocateReservedCharacterAsync(
                reservation, CancellationToken).AsTask();
            Assert.False(pending.IsCompleted);
            reservation.Dispose();
            Assert.True(reservation.IsActive);
            Assert.Null(await runtime.TryWalkCharacterAsync(source, 0, CancellationToken));
            release.Set();
            Assert.True(await blocker);
            WorldCharacterOwnership destination = Assert.IsType<WorldCharacterOwnership>(await pending);
            Assert.False(reservation.IsActive);
            Assert.NotNull(await runtime.ReadCharacterAsync(destination, CancellationToken));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CancellationBeforeDispatch_KeepsOriginalOwnerAndCapacity()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.TryRelocateReservedCharacterAsync(reservation, cancelled.Token).AsTask());
        Assert.True(reservation.IsActive);
        reservation.Dispose();
        Assert.NotNull(await runtime.TryWalkCharacterAsync(source, 0, CancellationToken));
        WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), CancellationToken);
        Assert.NotNull(await runtime.ReadCharacterAsync(other, CancellationToken));
    }

    [Fact]
    public async Task DuplicateRelocationAttempt_CannotPublishAnotherGeneration()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));

        Task<WorldCharacterOwnership?> first = runtime.TryRelocateReservedCharacterAsync(
            reservation, CancellationToken).AsTask();
        Assert.Null(await runtime.TryRelocateReservedCharacterAsync(reservation, CancellationToken));
        WorldCharacterOwnership destination = Assert.IsType<WorldCharacterOwnership>(await first);
        Assert.Equal(1, runtime.ResidentCount);
        Assert.Null(await runtime.ReadCharacterAsync(source, CancellationToken));
        Assert.NotNull(await runtime.ReadCharacterAsync(destination, CancellationToken));
    }

    [Fact]
    public async Task ShutdownWhileDestinationIsQueued_RejectsCutoverAndClearsResidency()
    {
        await using WorldRuntime runtime = CreateRuntime(maxPerMap: 2);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), CancellationToken);
        WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), CancellationToken);
        using WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, CancellationToken));
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        ValueTask<bool> blocker = runtime.TryPublishAvatarActionAsync(other, _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The target worker was never released.");
            }
        }, CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), CancellationToken));
            Task<WorldCharacterOwnership?> pending = runtime.TryRelocateReservedCharacterAsync(
                reservation, CancellationToken).AsTask();
            Task stopped = runtime.StopAsync();
            release.Set();
            Assert.True(await blocker);
            await Assert.ThrowsAnyAsync<Exception>(() => pending);
            await stopped;
            Assert.False(reservation.IsActive);
            Assert.Equal(0, runtime.ResidentCount);
        }
        finally
        {
            release.Set();
        }
    }

    private static WorldRuntime CreateRuntime(int maxPerMap)
    {
        WorldMapContentCatalog catalog = WorldMapTestContent.CreateCatalog(
            CreateMap(SourceMapId, 1015), CreateMap(TargetMapId, 1016));
        return new WorldRuntime(catalog, new WorldExecutionOptions(1, 8, 1, 4, maxPerMap));
    }

    private static WorldMapContent CreateMap(uint mapId, uint terrainId)
    {
        MapBaseTerrain terrain = new(terrainId, 3, 3,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 9), []);
        return new WorldMapContent(new MapDefinition(mapId, terrainId, 0),
            new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile Profile(uint characterId, uint mapId)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "ResidencyTest");
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
