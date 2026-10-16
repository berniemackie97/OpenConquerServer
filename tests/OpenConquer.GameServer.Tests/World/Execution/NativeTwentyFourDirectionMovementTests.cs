using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class NativeTwentyFourDirectionMovementTests
{
    private const uint MapId = 1002;
    private const uint PlayerId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint MapDataId = 1015;

    [Theory]
    [InlineData(0, 2, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 2, 0)]
    [InlineData(5, 4, 0)]
    [InlineData(6, 4, 2)]
    [InlineData(7, 4, 4)]
    [InlineData(8, 1, 4)]
    [InlineData(9, 0, 3)]
    [InlineData(10, 0, 1)]
    [InlineData(11, 1, 0)]
    [InlineData(12, 3, 0)]
    [InlineData(13, 4, 1)]
    [InlineData(14, 4, 3)]
    [InlineData(15, 3, 4)]
    [InlineData(16, 1, 4)]
    [InlineData(17, 0, 3)]
    [InlineData(18, 0, 1)]
    [InlineData(19, 1, 0)]
    [InlineData(20, 3, 0)]
    [InlineData(21, 4, 1)]
    [InlineData(22, 4, 3)]
    [InlineData(23, 3, 4)]
    public void Partition_UsesAllVerifiedNative24Deltas(byte direction, ushort expectedX, ushort expectedY)
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(PlayerId, MapId, 1);
        map.Admit(owner, 2, 2);

        WorldCharacterSnapshot moved = Assert.IsType<WorldCharacterSnapshot>(map.TryWalk(owner, direction, 9));
        Assert.Equal((expectedX, expectedY), (moved.X, moved.Y));
        Assert.Equal(moved, Assert.IsType<WorldCharacterSnapshot>(map.Find(owner)));
    }

    [Fact]
    public void Partition_RejectsInvalid24IndexAndStaleOwnershipWithoutMutation()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(PlayerId, MapId, 2);
        map.Admit(owner, 2, 2);

        Assert.Null(map.TryWalk(owner, 24, 9));
        Assert.Null(map.TryWalk(owner, byte.MaxValue, 9));
        Assert.Null(map.TryWalk(owner with { Generation = 1 }, 0, 9));
        Assert.Equal((2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public void Partition_RejectsComposedBlockedDestinationWithoutMutatingPosition()
    {
        MapInstance map = new(CreateMap(blockedDestination: true));
        WorldCharacterOwnership owner = new(PlayerId, MapId, 1);
        map.Admit(owner, 2, 2);

        Assert.Equal((ushort)0, map.Map.BaseTerrain.GetCell(4, 2).PassabilityFlag);
        Assert.Null(map.TryWalk(owner, 6, 9));
        Assert.Equal((2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public void Partition_DoesNotLeapAStaticallyBlockedIntermediateTile()
    {
        MapInstance map = new(CreateMap(blockedIntermediate: true));
        WorldCharacterOwnership owner = new(PlayerId, MapId, 1);
        map.Admit(owner, 2, 2);

        Assert.True(map.Map.Collision.IsStaticallyPassable(4, 2));
        Assert.Null(map.TryWalk(owner, 6, 9));
        Assert.Equal((2, 2), Position(map.Find(owner)));
    }

    [Fact]
    public void Partition_RejectsOutOfBoundsTwoTileDestination()
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(PlayerId, MapId, 1);
        map.Admit(owner, 0, 0);

        Assert.Null(map.TryWalk(owner, 3, 9));
        Assert.Equal((0, 0), Position(map.Find(owner)));
    }

    [Theory]
    [InlineData(8u)]
    [InlineData(10u)]
    [InlineData(0x0000_0109u)]
    public void Partition_NonNineFullWidthSelectorUsesEightDirectionTable(uint selector)
    {
        MapInstance map = new(CreateMap());
        WorldCharacterOwnership owner = new(PlayerId, MapId, 1);
        map.Admit(owner, 2, 2);

        Assert.Null(map.TryWalk(owner, 8, selector));
        WorldCharacterSnapshot step = Assert.IsType<WorldCharacterSnapshot>(map.TryWalk(owner, 6, selector));
        Assert.Equal((3, 2), (step.X, step.Y));
    }

    [Fact]
    public async Task Runtime_RejectsMountedSelectorWithoutServerOwnedRideState()
    {
        WorldMapContentCatalog content = new([CreateMap()]);
        await using WorldRuntime runtime = new(content, new WorldExecutionOptions(1, 32, 8));
        runtime.Start();
        WorldCharacterOwnership owner = await runtime.AdmitCharacterAsync(CreateProfile(), TestContext.Current.CancellationToken);

        // Direction-table correctness is covered above independently of admission.
        // A normal authenticated character must not acquire mounted speed by
        // submitting selector 9 while no server-authoritative ride state exists.
        Assert.Null(await runtime.TryWalkCharacterAsync(owner, 23, 9,
            TestContext.Current.CancellationToken));
        Assert.Equal((2, 2), Position(await runtime.ReadCharacterAsync(owner, TestContext.Current.CancellationToken)));
        Assert.Null(await runtime.TryWalkCharacterAsync(owner with { Generation = owner.Generation + 1 }, 0, 9,
            TestContext.Current.CancellationToken));

        WorldCharacterSnapshot groundStep = Assert.IsType<WorldCharacterSnapshot>(
            await runtime.TryWalkCharacterAsync(owner, 6, 0, TestContext.Current.CancellationToken));
        Assert.Equal((3, 2), (groundStep.X, groundStep.Y));
        Assert.True(await runtime.RemoveCharacterAsync(owner, TestContext.Current.CancellationToken));
    }

    private static (int X, int Y) Position(WorldCharacterSnapshot? snapshot)
    {
        WorldCharacterSnapshot value = Assert.IsType<WorldCharacterSnapshot>(snapshot);
        return (value.X, value.Y);
    }

    private static WorldMapContent CreateMap(bool blockedDestination = false, bool blockedIntermediate = false)
    {
        MapBaseTerrain terrain = new(MapDataId, 5, 5,
            Enumerable.Repeat(new MapBaseTerrainCell(0, 0, 0), 25), []);
        MapTerrainCollisionOverride[] overrides = [];
        if (blockedDestination)
        {
            overrides = [new MapTerrainCollisionOverride(14, new MapBaseTerrainCell(0, 1, 0))];
        }
        else if (blockedIntermediate)
        {
            overrides = [new MapTerrainCollisionOverride(13, new MapBaseTerrainCell(0, 1, 0))];
        }

        MapTerrainCollision collision = new(terrain, overrides);
        return new WorldMapContent(new MapDefinition(MapId, MapDataId, 0), collision);
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(PlayerId, 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(MapId, 2, 2);
        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals,
            economy, 0, 0, 0, location);
    }
}
