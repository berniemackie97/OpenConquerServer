using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.World.Execution;

public sealed class MapInstanceTests
{
    private const uint CharacterId = 1_000_000;
    private const uint MapId = 1002;
    private const uint MapDataId = 1015;

    [Fact]
    public void Constructor_PreservesCanonicalWorldContent()
    {
        var content = WorldMapTestContent.Create(MapId, MapDataId, 0x1122334455667788);
        MapInstance map = new(content);

        Assert.Same(content, map.Map);
        Assert.Same(content.Collision, map.Map.Collision);
        Assert.Same(content.BaseTerrain, map.Map.BaseTerrain);
        Assert.Equal(MapId, map.Map.MapId);
        Assert.Equal(MapDataId, map.Map.MapDataId);
        Assert.Equal(0x1122334455667788UL, map.Map.Flags);
    }

    [Fact]
    public void Admit_StoresAuthoritativeSnapshotAndPreservesOwnership()
    {
        MapInstance map = CreateMap();
        WorldCharacterOwnership ownership = new(CharacterId, MapId, 1);

        map.Admit(ownership, 0, 0);

        WorldCharacterSnapshot snapshot = Assert.IsType<WorldCharacterSnapshot>(map.Find(ownership));

        Assert.Equal(ownership, snapshot.Ownership);
        Assert.Equal((ushort)0, snapshot.X);
        Assert.Equal((ushort)0, snapshot.Y);
    }

    [Fact]
    public void Admit_DuplicateCharacter_IsRejectedWithoutReplacingOriginal()
    {
        MapInstance map = CreateMap();
        WorldCharacterOwnership original = new(CharacterId, MapId, 1);
        WorldCharacterOwnership replacement = new(CharacterId, MapId, 2);

        map.Admit(original, 0, 0);

        Assert.Throws<InvalidOperationException>(() => map.Admit(replacement, 0, 0));
        Assert.NotNull(map.Find(original));
        Assert.Null(map.Find(replacement));
    }

    [Theory]
    [InlineData(1003u, 1_000_000u, 1UL)]
    [InlineData(1002u, 999_999u, 1UL)]
    [InlineData(1002u, 1_000_000u, 0UL)]
    public void Admit_InvalidOwnership_IsRejected(uint mapId, uint characterId, ulong generation)
    {
        MapInstance map = CreateMap();
        WorldCharacterOwnership ownership = new(characterId, mapId, generation);

        Assert.Throws<ArgumentException>(() => map.Admit(ownership, 0, 0));
        Assert.Null(map.Find(ownership));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(ushort.MaxValue, ushort.MaxValue)]
    public void Admit_OutOfBoundsPosition_IsRejected(ushort x, ushort y)
    {
        MapInstance map = CreateMap();

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Admit(new WorldCharacterOwnership(CharacterId, MapId, 1), x, y));
    }

    [Fact]
    public void Remove_RequiresExactOwnershipGeneration()
    {
        MapInstance map = CreateMap();
        WorldCharacterOwnership current = new(CharacterId, MapId, 7);
        WorldCharacterOwnership stale = new(CharacterId, MapId, 6);

        map.Admit(current, 0, 0);

        Assert.False(map.Remove(stale));
        Assert.NotNull(map.Find(current));
        Assert.Null(map.Find(stale));
        Assert.True(map.Remove(current));
        Assert.False(map.Remove(current));
        Assert.Null(map.Find(current));
    }

    [Fact]
    public void Clear_DiscardsMapLocalCharactersWithoutChangingCanonicalContent()
    {
        MapInstance map = CreateMap();
        WorldCharacterOwnership ownership = new(CharacterId, MapId, 1);
        var content = map.Map;

        map.Admit(ownership, 0, 0);
        map.Clear();

        Assert.Null(map.Find(ownership));
        Assert.Same(content, map.Map);
        Assert.Same(content.Collision, map.Map.Collision);
    }

    private static MapInstance CreateMap() => new(WorldMapTestContent.Create(MapId, MapDataId, 0));
}
