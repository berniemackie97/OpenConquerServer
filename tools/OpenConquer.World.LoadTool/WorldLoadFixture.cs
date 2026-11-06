using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;

namespace OpenConquer.World.LoadTool;

internal static class WorldLoadFixture
{
    // Synthetic IDs and a 3x3 flat grid. No retail map files, accounts or database are loaded.
    public const uint FirstMapId = 900_000;

    public static WorldMapContentCatalog CreateMaps(int count)
    {
        WorldMapContent[] maps = new WorldMapContent[count];
        for (int index = 0; index < count; index++)
        {
            uint id = FirstMapId + (uint)index;
            MapBaseTerrainCell[] cells = new MapBaseTerrainCell[9];
            MapBaseTerrain terrain = new(id, 3, 3, cells, []);
            maps[index] = new WorldMapContent(new MapDefinition(id, id, 0), new MapTerrainCollision(terrain, []));
        }

        return new WorldMapContentCatalog(maps);
    }

    public static CharacterLoginProfile CreateCharacter(int index, int mapCount)
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId + (uint)index;
        return new CharacterLoginProfile(new CharacterLoginIdentity(characterId, (uint)index + 1, "LoadProbe"),
            new CharacterAppearance(1003, 410), new CharacterProgression(1, 0, 10, 0, 0, 0, 0),
            new CharacterAttributes(10, 10, 10, 10, 0), new CharacterVitals(100, 0), new CharacterEconomy(0, 0, 0),
            0, 0, 0, new CharacterLocation(FirstMapId + (uint)(index % mapCount), 1, 1));
    }
}
