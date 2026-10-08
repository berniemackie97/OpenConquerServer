using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class MapBaseTerrainCatalog
{
    private readonly FrozenDictionary<uint, MapBaseTerrain> _terrains;

    public MapBaseTerrainCatalog(IEnumerable<MapBaseTerrain> terrains)
    {
        ArgumentNullException.ThrowIfNull(terrains);

        Dictionary<uint, MapBaseTerrain> indexed = [];

        foreach (MapBaseTerrain terrain in terrains)
        {
            if (terrain is null)
            {
                throw new ArgumentException("A terrain catalog cannot contain a null terrain.", nameof(terrains));
            }

            if (!indexed.TryAdd(terrain.MapDataId, terrain))
            {
                throw new ArgumentException($"Terrain catalog contains duplicate MapDataId {terrain.MapDataId}.", nameof(terrains));
            }
        }

        if (indexed.Count == 0)
        {
            throw new ArgumentException("A terrain catalog cannot be empty.", nameof(terrains));
        }

        _terrains = indexed.ToFrozenDictionary();
    }

    public int Count => _terrains.Count;

    public bool TryGet(uint mapDataId, [NotNullWhen(true)] out MapBaseTerrain? terrain)
    {
        return _terrains.TryGetValue(mapDataId, out terrain);
    }
}
