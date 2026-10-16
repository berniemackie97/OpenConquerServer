using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldJumpTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;

    [Theory]
    [InlineData(2, 2, 18, 2, true)]
    [InlineData(2, 2, 18, 6, true)] // sqrt(272) rounds to 16
    [InlineData(2, 2, 18, 7, false)] // sqrt(281) rounds to 17
    [InlineData(2, 2, 19, 2, false)]
    [InlineData(2, 2, 2, 2, false)]
    public void Map_UsesNativeRoundedDistanceGate(ushort fromX, ushort fromY,
        ushort toX, ushort toY, bool allowed)
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, fromX, fromY);
        WorldCharacterSnapshot? moved = map.TryJump(owner, toX, toY);
        Assert.Equal(allowed, moved.HasValue);
        Assert.Equal(allowed ? (toX, toY) : (fromX, fromY), Position(map.Find(owner)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Map_RejectsProhibitedMapOrBlockedLanding(bool forbidden, bool blocked)
    {
        MapInstance map = new(CreateMap(forbidden, blocked));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);
        Assert.Null(map.TryJump(owner, 5, 5));
        Assert.Equal((2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public void Map_RejectsOutOfBoundsAndStaleGeneration()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);
        Assert.Null(map.TryJump(owner, 64, 2));
        Assert.Null(map.TryJump(owner with { Generation = 2 }, 3, 2));
        Assert.Equal((2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public async Task Runtime_SerializesJumpUnderGenerationFence()
    {
        WorldMapContentCatalog maps = new([CreateMap()]);
        await using WorldRuntime runtime = new(maps, new WorldExecutionOptions(1, 32, 8));
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);
        WorldCharacterSnapshot moved = Assert.IsType<WorldCharacterSnapshot>(await runtime.TryJumpCharacterAsync(owner, 8, 8,
            TestContext.Current.CancellationToken));
        Assert.Equal((8, 8), (moved.X, moved.Y));
        Assert.Null(await runtime.TryJumpCharacterAsync(owner with { Generation = owner.Generation + 1 }, 9, 9,
            TestContext.Current.CancellationToken));
        Assert.Equal((8, 8), Position(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken)));
        Assert.True(await runtime.RemoveCharacterAsync(owner, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MovementPublicationFinishesBeforeNextPartitionTurn()
    {
        WorldMapContentCatalog maps = new([CreateMap()]);
        await using WorldRuntime runtime = new(maps, new WorldExecutionOptions(1, 32, 8));
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(),
            TestContext.Current.CancellationToken);
        using ManualResetEventSlim publishing = new(false);
        using ManualResetEventSlim release = new(false);

        try
        {
            ValueTask<WorldCharacterSnapshot?> first = runtime.TryWalkCharacterAsync(owner, 6, 0,
                _ =>
                {
                    publishing.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(8)))
                    {
                        throw new TimeoutException("Partition movement-publication test barrier expired.");
                    }
                }, TestContext.Current.CancellationToken);
            Assert.True(publishing.Wait(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

            // A second movement of the same character cannot execute while
            // the first publication is held at this deterministic barrier.
            ValueTask<WorldCharacterSnapshot?> second = runtime.TryWalkCharacterAsync(owner, 0, 0,
                _ => Assert.True(release.IsSet), TestContext.Current.CancellationToken);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            release.Set();

            WorldCharacterSnapshot moved = Assert.IsType<WorldCharacterSnapshot>(await first);
            WorldCharacterSnapshot movedAgain = Assert.IsType<WorldCharacterSnapshot>(await second);
            Assert.Equal((3, 2), (moved.X, moved.Y));
            Assert.Equal((3, 3), (movedAgain.X, movedAgain.Y));
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData((short)200, true)]
    [InlineData((short)201, false)]
    public void GroundJump_EnforcesNativePeakElevationAtInclusiveBoundary(short peak, bool permitted)
    {
        MapTerrainCollisionOverride[] overrides =
            [new(2 * 64 + 4, new MapBaseTerrainCell(0, 0, peak))];
        MapInstance map = new(CreateMap(overrides: overrides));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);
        Assert.Equal(permitted, map.TryJump(owner, 6, 2).HasValue);
        Assert.Equal(permitted ? (6, 2) : (2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public void GroundJump_SamplesComposedDiagonalAndAllowsDescendingTerrain()
    {
        MapTerrainCollisionOverride[] overrides =
        [
            new(2 * 64 + 2, new MapBaseTerrainCell(0, 0, -100)),
            new(4 * 64 + 4, new MapBaseTerrainCell(0, 0, 101)),
        ];
        MapInstance map = new(CreateMap(overrides: overrides));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);
        Assert.Null(map.TryJump(owner, 6, 6)); // 201 above the source.
        Assert.Equal((2, 2), Position(map.Find(owner)));
        Assert.NotNull(map.TryJump(owner, 2, 6)); // A descent is not a forbidden climb.
    }

    [Fact]
    public void GroundJump_BlocksLivingOccupantAndReleasesTileAfterMovementOrRemoval()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership mover = new(CharacterId, MapId, 1);
        WorldCharacterOwnership occupant = new(CharacterId + 1, MapId, 2);
        map.Admit(mover, 2, 2);
        map.Admit(occupant, 6, 2);
        Assert.Null(map.TryJump(mover, 6, 2));
        Assert.NotNull(map.TryWalk(occupant, 6));
        Assert.NotNull(map.TryJump(mover, 6, 2));
        Assert.True(map.Remove(occupant));
        Assert.NotNull(map.TryJump(mover, 8, 2));
    }

    [Fact]
    public void GroundJump_IgnoresDeadLandingOccupantsAndRejectsDeadJumper()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership mover = new(CharacterId, MapId, 1);
        WorldCharacterOwnership dead = new(CharacterId + 1, MapId, 2);
        map.Admit(mover, 2, 2);
        map.Admit(dead, 6, 2, isAlive: false);
        Assert.NotNull(map.TryJump(mover, 6, 2));
        Assert.Null(map.TryJump(dead, 7, 2));
    }

    [Fact]
    public async Task RuntimeAdmission_DerivesLandingOccupancyFromPersistedLife()
    {
        WorldMapContentCatalog maps = new([CreateMap()]);
        await using WorldRuntime runtime = new(maps, new WorldExecutionOptions(1, 32, 8));
        runtime.Start();
        WorldCharacterOwnership jumper = await runtime.AdmitCharacterAsync(CreateProfile(),
            TestContext.Current.CancellationToken);
        WorldCharacterOwnership dead = await runtime.AdmitCharacterAsync(
            CreateProfile(characterId: CharacterId + 1, life: 0, x: 6, y: 2),
            TestContext.Current.CancellationToken);
        WorldCharacterSnapshot deadSnapshot = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.ReadCharacterAsync(dead, TestContext.Current.CancellationToken));
        Assert.False(deadSnapshot.IsAlive);
        Assert.NotNull(await runtime.TryJumpCharacterAsync(jumper, 6, 2,
            TestContext.Current.CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(dead, TestContext.Current.CancellationToken));
        Assert.True(await runtime.RemoveCharacterAsync(jumper, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GroundJump_LivingOccupancyReferenceCountsSharedWalkingTiles()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership jumper = new(CharacterId, MapId, 1);
        WorldCharacterOwnership first = new(CharacterId + 1, MapId, 2);
        WorldCharacterOwnership second = new(CharacterId + 2, MapId, 3);
        map.Admit(jumper, 2, 2);
        map.Admit(first, 6, 2);
        map.Admit(second, 5, 2);
        Assert.NotNull(map.TryWalk(second, 6));
        Assert.True(map.Remove(first));
        Assert.Null(map.TryJump(jumper, 6, 2));
        Assert.True(map.Remove(second));
        Assert.NotNull(map.TryJump(jumper, 6, 2));
    }

    private static (ushort, ushort) Position(WorldCharacterSnapshot? snapshot)
    {
        WorldCharacterSnapshot value = Assert.IsType<WorldCharacterSnapshot>(snapshot);
        return (value.X, value.Y);
    }

    private static WorldMapContent CreateMap(bool forbidden = false, bool blocked = false,
        MapTerrainCollisionOverride[]? overrides = null)
    {
        const int size = 64;
        MapBaseTerrain terrain = new(1015, size, size,
            Enumerable.Repeat(new MapBaseTerrainCell(0, 0, 0), size * size), []);
        MapTerrainCollisionOverride[] composed = overrides ?? [];
        if (blocked && overrides is null)
        {
            composed = [new MapTerrainCollisionOverride(5 * size + 5, new MapBaseTerrainCell(0, 1, 0))];
        }
        return new WorldMapContent(new MapDefinition(MapId, 1015, forbidden ? 0x0200_0000UL : 0),
            new MapTerrainCollision(terrain, composed));
    }

    private static CharacterLoginProfile CreateProfile(uint characterId = CharacterId, ushort life = 100,
        ushort x = 2, ushort y = 2)
    {
        CharacterLoginIdentity identity = new(characterId, 42, "Bernie");
        CharacterAppearance appearance = new(1003, 410);
        CharacterProgression progression = new(1, 0, 10, 0, 0, 0, 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(life, 0);
        CharacterEconomy economy = new(0, 0, 0);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals,
            economy, 0, 0, 0, new CharacterLocation(MapId, x, y));
    }
}
