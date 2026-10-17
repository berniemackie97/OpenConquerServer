using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldTransferReservationTests
{
    private const uint SourceMapId = 1002;
    private const uint TargetMapId = 1011;
    private const uint ThirdMapId = 1000;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;

    [Fact]
    public async Task Reservation_FencesMovementAndAvatarActionsUntilDisposed()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 3, maxCharactersPerMap: 2);
        runtime.Start();
        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);

        WorldTransferReservation reservation = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(ownership, TargetMapId, 1, 1, TestContext.Current.CancellationToken));
        using (reservation)
        {
            Assert.True(reservation.IsActive);
            Assert.Equal(ownership, reservation.SourceSnapshot.Ownership);
            Assert.Equal((ushort)1, reservation.SourceSnapshot.X);
            Assert.Equal((ushort)1, reservation.SourceSnapshot.Y);
            Assert.Null(await runtime.TryWalkCharacterAsync(ownership, 0, TestContext.Current.CancellationToken));
            Assert.Null(await runtime.TryJumpCharacterAsync(ownership, 2, 2, TestContext.Current.CancellationToken));
            int published = 0;
            Assert.False(await runtime.TryPublishAvatarActionAsync(ownership, _ => published++, TestContext.Current.CancellationToken));
            Assert.Equal(0, published);
            Assert.Equal(ownership, (await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken))?.Ownership);
        }

        Assert.False(reservation.IsActive);
        int afterRelease = 0;
        Assert.True(await runtime.TryPublishAvatarActionAsync(ownership, _ => afterRelease++, TestContext.Current.CancellationToken));
        Assert.Equal(1, afterRelease);
        Assert.NotNull(await runtime.TryWalkCharacterAsync(ownership, 0, TestContext.Current.CancellationToken));
        Assert.Equal((ushort)2, Assert.IsType<WorldCharacterSnapshot>(await runtime.ReadCharacterAsync(ownership, TestContext.Current.CancellationToken)).Y);
    }

    [Fact]
    public async Task ReservationBarrier_CapturesEarlierAcceptedMovementBeforeFencing()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership ownership = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);

        // Hold the source worker inside a prior partition turn. The movement and
        // preflight barrier must both be pending when the preflight is enqueued.
        ValueTask<bool> blocker = runtime.TryPublishAvatarActionAsync(ownership, _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The controlled test worker was never released.");
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            ValueTask<WorldCharacterSnapshot?> previousStep = runtime.TryWalkCharacterAsync(ownership, 0, TestContext.Current.CancellationToken);
            Assert.False(previousStep.IsCompleted);
            ValueTask<WorldTransferReservation?> preflight = runtime.TryReserveTransferAsync(
                ownership, TargetMapId, 1, 1, TestContext.Current.CancellationToken);
            Assert.False(preflight.IsCompleted);

            // The fence must reject commands after preflight, even before the
            // previously accepted movement and snapshot barrier have executed.
            Assert.Null(await runtime.TryWalkCharacterAsync(ownership, 4, TestContext.Current.CancellationToken));
            release.Set();
            Assert.True(await blocker);
            WorldCharacterSnapshot committed = Assert.IsType<WorldCharacterSnapshot>(await previousStep);
            using WorldTransferReservation lease = Assert.IsType<WorldTransferReservation>(await preflight);
            Assert.Equal((ushort)2, committed.Y);
            Assert.Equal(committed, lease.SourceSnapshot);
            Assert.Null(await runtime.TryWalkCharacterAsync(ownership, 4, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Reservation_ConsumesTargetMapCapacityButNotGlobalCharacterCapacity()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        WorldTransferReservation lease = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, TestContext.Current.CancellationToken));
        try
        {
            Assert.Equal(1, runtime.ResidentCount);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken).AsTask());
            Assert.Null(await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, TestContext.Current.CancellationToken));
            Assert.NotNull(await runtime.ReadCharacterAsync(source, TestContext.Current.CancellationToken));

            // A destination reservation consumes target-map capacity, not a
            // second global resident slot. This admission must actually succeed.
            WorldCharacterOwnership third = await runtime.AdmitCharacterAsync(
                Profile(CharacterId + 2, ThirdMapId), TestContext.Current.CancellationToken);
            Assert.Equal(2, runtime.ResidentCount);
            Assert.NotNull(await runtime.ReadCharacterAsync(third, TestContext.Current.CancellationToken));
            Assert.True(await runtime.RemoveCharacterAsync(third, TestContext.Current.CancellationToken));
        }
        finally
        {
            lease.Dispose();
            lease.Dispose();
        }

        WorldCharacterOwnership other = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
        Assert.NotNull(await runtime.ReadCharacterAsync(other, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Removal_RevokesReservationAndRefundsDestinationCapacity()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        using WorldTransferReservation lease = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, TestContext.Current.CancellationToken));

        Assert.True(await runtime.RemoveCharacterAsync(source, TestContext.Current.CancellationToken));
        Assert.False(lease.IsActive);
        Assert.Null(await runtime.ReadCharacterAsync(source, TestContext.Current.CancellationToken));
        WorldCharacterOwnership replacement = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
        Assert.NotNull(await runtime.ReadCharacterAsync(replacement, TestContext.Current.CancellationToken));
        lease.Dispose(); // A revoked lease must never refund the new occupant.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AdmitCharacterAsync(Profile(CharacterId + 2, TargetMapId), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task InvalidDestinationAndStaleOwnershipDoNotFreezeOwner()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        WorldCharacterOwnership stale = source with { Generation = source.Generation + 1 };

        Assert.Null(await runtime.TryReserveTransferAsync(stale, TargetMapId, 1, 1, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryReserveTransferAsync(source, SourceMapId, 1, 1, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryReserveTransferAsync(source, 9999, 1, 1, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryReserveTransferAsync(source, TargetMapId, 3, 1, TestContext.Current.CancellationToken));
        Assert.True(await runtime.TryPublishAvatarActionAsync(source, _ => { }, TestContext.Current.CancellationToken));
        WorldCharacterOwnership target = await runtime.AdmitCharacterAsync(Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
        Assert.NotNull(await runtime.ReadCharacterAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellationAndShutdownRevokeReservationWithoutExposingStaleAuthority()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1);
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.TryReserveTransferAsync(source, TargetMapId, 0, 0, cancelled.Token).AsTask());

        using WorldTransferReservation lease = Assert.IsType<WorldTransferReservation>(
            await runtime.TryReserveTransferAsync(source, TargetMapId, 1, 1, TestContext.Current.CancellationToken));
        await runtime.StopAsync();
        Assert.False(lease.IsActive);
        lease.Dispose();
    }

    [Fact]
    public async Task StalledBarrier_TimesOutAndRefundsTargetSlotWithoutKeepingSourceFrozen()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2, maxCharactersPerMap: 1,
            transferPreflightTimeout: TimeSpan.FromMilliseconds(150));
        runtime.Start();
        WorldCharacterOwnership source = await runtime.AdmitCharacterAsync(Profile(CharacterId, SourceMapId), TestContext.Current.CancellationToken);
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        ValueTask<bool> blocker = runtime.TryPublishAvatarActionAsync(source, _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The controlled test worker was never released.");
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Task<WorldTransferReservation?> preflight = runtime.TryReserveTransferAsync(
                source, TargetMapId, 1, 1, TestContext.Current.CancellationToken).AsTask();
            Assert.False(preflight.IsCompleted);
            await Assert.ThrowsAsync<TimeoutException>(() => preflight);

            // The worker is still blocked: these operations can only enqueue
            // successfully if the timeout released both the fence and slot.
            ValueTask<WorldCharacterSnapshot?> resumedWalk = runtime.TryWalkCharacterAsync(source, 0, TestContext.Current.CancellationToken);
            Assert.False(resumedWalk.IsCompleted);
            ValueTask<WorldCharacterOwnership> targetAdmission = runtime.AdmitCharacterAsync(
                Profile(CharacterId + 1, TargetMapId), TestContext.Current.CancellationToken);
            release.Set();
            Assert.True(await blocker);
            Assert.NotNull(await resumedWalk);
            WorldCharacterOwnership admitted = await targetAdmission;
            Assert.Equal(TargetMapId, admitted.MapId);
        }
        finally
        {
            release.Set();
        }
    }

    private static WorldRuntime CreateRuntime(int maxCharacters, int maxCharactersPerMap, TimeSpan? transferPreflightTimeout = null)
    {
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(
            CreateMap(SourceMapId, 1015), CreateMap(TargetMapId, 1016), CreateMap(ThirdMapId, 1017));
        return new WorldRuntime(maps, new WorldExecutionOptions(1, 4, 1, maxCharacters, maxCharactersPerMap, transferPreflightTimeout));
    }

    private static WorldMapContent CreateMap(uint mapId, uint mapDataId)
    {
        MapBaseTerrain terrain = new(mapDataId, 3, 3,
            Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 9), []);
        return new WorldMapContent(new MapDefinition(mapId, mapDataId, 0),
            new MapTerrainCollision(terrain, []));
    }

    private static CharacterLoginProfile Profile(uint characterId, uint mapId)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "TransferTest");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId, 1, 1);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, 0, 0, 0, location);
    }
}
