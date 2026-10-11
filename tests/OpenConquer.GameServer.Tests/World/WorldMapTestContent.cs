using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.GameServer.Tests.World;

internal static class WorldMapTestContent
{
    public static WorldMapContent Create(uint mapId, uint mapDataId, ulong flags)
    {
        MapBaseTerrain terrain = new(mapDataId, 1, 1, [new MapBaseTerrainCell(10, 0, -5)], []);
        MapTerrainCollision collision = new(terrain, []);

        return new WorldMapContent(new MapDefinition(mapId, mapDataId, flags), collision);
    }

    public static WorldMapContentCatalog CreateCatalog(params WorldMapContent[] maps)
    {
        return new WorldMapContentCatalog(maps);
    }
}
