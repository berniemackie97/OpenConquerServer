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

    private static (ushort, ushort) Position(WorldCharacterSnapshot? snapshot)
    {
        WorldCharacterSnapshot value = Assert.IsType<WorldCharacterSnapshot>(snapshot);
        return (value.X, value.Y);
    }

    private static WorldMapContent CreateMap(bool forbidden = false, bool blocked = false)
    {
        const int size = 64;
        MapBaseTerrain terrain = new(1015, size, size,
            Enumerable.Repeat(new MapBaseTerrainCell(0, 0, 0), size * size), []);
        MapTerrainCollisionOverride[] overrides = blocked
            ? [new MapTerrainCollisionOverride(5 * size + 5, new MapBaseTerrainCell(0, 1, 0))]
            : [];
        return new WorldMapContent(new MapDefinition(MapId, 1015, forbidden ? 0x0200_0000UL : 0),
            new MapTerrainCollision(terrain, overrides));
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, 42, "Bernie");
        CharacterAppearance appearance = new(1003, 410);
        CharacterProgression progression = new(1, 0, 10, 0, 0, 0, 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals,
            economy, 0, 0, 0, new CharacterLocation(MapId, 2, 2));
    }
}
