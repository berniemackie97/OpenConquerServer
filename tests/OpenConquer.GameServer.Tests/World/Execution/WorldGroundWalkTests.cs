using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class WorldGroundWalkTests
{
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapId = 1002;
    private const uint OtherMapId = 1011;

    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(1, 1, 3)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 2, 1)]
    [InlineData(5, 3, 1)]
    [InlineData(6, 3, 2)]
    [InlineData(7, 3, 3)]
    public void TryWalk_Native5517DirectionOrder_CommitsOneTile(byte direction, ushort expectedX, ushort expectedY)
    {
        MapInstance map = new(CreateMap(MapId, 1015));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);

        WorldCharacterSnapshot moved = Assert.IsType<WorldCharacterSnapshot>(map.TryWalk(owner, direction));
        Assert.Equal((expectedX, expectedY), (moved.X, moved.Y));
        Assert.Equal(owner, moved.Ownership);
        Assert.Equal(moved, Assert.IsType<WorldCharacterSnapshot>(map.Find(owner)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(byte.MaxValue)]
    public void TryWalk_InvalidDirection_DoesNotChangePosition(byte direction)
    {
        MapInstance map = new(CreateMap(MapId, 1015));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);

        Assert.Null(map.TryWalk(owner, direction));
        Assert.Equal((2, 2), GetPosition(map, owner));
    }

    [Fact]
    public void TryWalk_DeadCharacterCannotMoveOrMutateAuthoritativePosition()
    {
        MapInstance map = new(CreateMap(MapId, 1015));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2, isAlive: false);

        Assert.Null(map.TryWalk(owner, 6));
        Assert.Null(map.TryWalk(owner, 6, 9));
        Assert.Equal((2, 2), GetPosition(map, owner));
        Assert.False(Assert.IsType<WorldCharacterSnapshot>(map.Find(owner)).IsAlive);
    }

    [Fact]
    public async Task Runtime_DeadPersistedCharacterCannotWalk()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(2, 2, life: 0),
            TestContext.Current.CancellationToken);

        Assert.Null(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken));
        Assert.False(snapshot.IsAlive);
        Assert.Equal((ushort)2, snapshot.X);
        Assert.Equal((ushort)2, snapshot.Y);
        Assert.True(await runtime.RemoveCharacterAsync(owner, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TryWalk_ComposedSceneryBlock_RefusesWithoutChangingPosition()
    {
        MapInstance map = new(CreateMap(MapId, 1015, blocked: true));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 1);
        map.Admit(owner, 2, 2);

        Assert.Equal((ushort)0, map.Map.BaseTerrain.GetCell(3, 2).PassabilityFlag);
        Assert.Null(map.TryWalk(owner, 6));
        Assert.Equal((2, 2), GetPosition(map, owner));
        Assert.NotNull(map.TryWalk(owner, 0));
        Assert.Equal((2, 3), GetPosition(map, owner));
    }

    [Fact]
    public void TryWalk_OutOfBoundsAndStaleOwnership_AreNonMutating()
    {
        MapInstance map = new(CreateMap(MapId, 1015));
        WorldCharacterOwnership owner = new(CharacterId, MapId, 4);
        map.Admit(owner, 0, 0);

        Assert.Null(map.TryWalk(owner, 2));
        Assert.Null(map.TryWalk(owner, 4));
        Assert.Null(map.TryWalk(owner with { Generation = 3 }, 0));
        Assert.Null(map.TryWalk(owner with { MapId = OtherMapId }, 0));
        Assert.Null(map.TryWalk(new WorldCharacterOwnership(CharacterId + 1, MapId, 4), 0));
        Assert.Equal((0, 0), GetPosition(map, owner));
        Assert.True(map.Remove(owner));
        Assert.Null(map.TryWalk(owner, 0));
    }

    [Fact]
    public async Task TryWalkCharacterAsync_QueuedStepsCommitInOrderAndReadBackConsistently()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);

        ValueTask<WorldCharacterSnapshot?> first = runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken);
        ValueTask<WorldCharacterSnapshot?> second = runtime.TryWalkCharacterAsync(owner, 0, TestContext.Current.CancellationToken);
        ValueTask<WorldCharacterSnapshot?> third = runtime.TryWalkCharacterAsync(owner, 2, TestContext.Current.CancellationToken);

        Assert.Equal((3, 2), Position(await first));
        Assert.Equal((3, 3), Position(await second));
        Assert.Equal((2, 3), Position(await third));
        Assert.Equal((2, 3), Position(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken)));
        Assert.True(await runtime.RemoveCharacterAsync(owner, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryWalkCharacterAsync_StaleGenerationAndRuntimeCannotMoveReplacement()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        WorldCharacterOwnership old = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);
        Assert.True(await runtime.RemoveCharacterAsync(old, TestContext.Current.CancellationToken));

        WorldCharacterOwnership current = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);
        Assert.NotEqual(old.Generation, current.Generation);
        Assert.Null(await runtime.TryWalkCharacterAsync(old, 0, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryWalkCharacterAsync(current with { RuntimeId = Guid.NewGuid() }, 0, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryWalkCharacterAsync(current with { MapId = OtherMapId }, 0, TestContext.Current.CancellationToken));
        Assert.Equal((2, 2), Position(await runtime.ReadCharacterAsync(current, TestContext.Current.CancellationToken)));
        Assert.Equal((2, 3), Position(await runtime.TryWalkCharacterAsync(current, 0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TryWalkCharacterAsync_BlockedStepAndInvalidDirectionDoNotMutate()
    {
        await using WorldRuntime runtime = CreateRuntime(blocked: true);
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);

        Assert.Null(await runtime.TryWalkCharacterAsync(owner, 6, TestContext.Current.CancellationToken));
        Assert.Null(await runtime.TryWalkCharacterAsync(owner, 8, TestContext.Current.CancellationToken));
        Assert.Equal((2, 2), Position(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task TryWalkCharacterAsync_PreCancellationAndAcceptedCancellationHaveDistinctSemantics()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.TryWalkCharacterAsync(owner, 0, cancellation.Token).AsTask());
        Assert.Equal((2, 2), Position(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken)));

        using CancellationTokenSource afterAccept = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        ValueTask<WorldCharacterSnapshot?> accepted = runtime.TryWalkCharacterAsync(owner, 0, afterAccept.Token);
        afterAccept.Cancel();
        Assert.Equal((2, 3), Position(await accepted));
    }

    [Fact]
    public async Task TryWalkCharacterAsync_AfterShutdownIsRejected()
    {
        await using WorldRuntime runtime = CreateRuntime();
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(2, 2), TestContext.Current.CancellationToken);
        await runtime.StopAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.TryWalkCharacterAsync(owner, 0, TestContext.Current.CancellationToken).AsTask());
    }

    private static (int X, int Y) GetPosition(MapInstance map, WorldCharacterOwnership owner)
    {
        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(map.Find(owner));
        return (snapshot.X, snapshot.Y);
    }

    private static (int X, int Y) Position(WorldCharacterSnapshot? snapshot)
    {
        WorldCharacterSnapshot value = Assert.IsType<WorldCharacterSnapshot>(snapshot);
        return (value.X, value.Y);
    }

    private static WorldMapContent CreateMap(uint mapId, uint dataId, bool blocked = false)
    {
        MapBaseTerrain terrain = new(dataId, 5, 5, Enumerable.Repeat(new MapBaseTerrainCell(0, 0, 0), 25), []);
        MapTerrainCollision collision = new(terrain, blocked ? [new MapTerrainCollisionOverride(13, new MapBaseTerrainCell(0, 1, 0))] : []);
        return new WorldMapContent(new MapDefinition(mapId, dataId, 0), collision);
    }

    private static WorldRuntime CreateRuntime(bool blocked = false)
    {
        WorldMapContentCatalog maps = new([CreateMap(MapId, 1015, blocked), CreateMap(OtherMapId, 1016)]);
        return new WorldRuntime(maps, new WorldExecutionOptions(2, 32, 8));
    }

    private static CharacterLoginProfile CreateProfile(ushort x, ushort y, ushort life = 100)
    {
        CharacterLoginIdentity identity = new(CharacterId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(life, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(MapId, x, y);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, 0, 0, 0, location);
    }
}
