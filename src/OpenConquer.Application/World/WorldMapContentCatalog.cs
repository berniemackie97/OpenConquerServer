using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class WorldMapContentCatalog
{
    private readonly FrozenDictionary<uint, WorldMapContent> _maps;

    public WorldMapContentCatalog(IEnumerable<WorldMapContent> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);

        Dictionary<uint, WorldMapContent> indexed = [];
        Dictionary<uint, MapTerrainCollision> sharedCollisions = [];

        foreach (WorldMapContent map in maps)
        {
            if (map is null)
            {
                throw new ArgumentException("A world-map content catalog cannot contain a null map.", nameof(maps));
            }

            if (!indexed.TryAdd(map.MapId, map))
            {
                throw new ArgumentException($"World-map content catalog contains duplicate MapId {map.MapId}.", nameof(maps));
            }

            if (sharedCollisions.TryGetValue(map.MapDataId, out MapTerrainCollision? existing))
            {
                if (!ReferenceEquals(existing, map.Collision))
                {
                    throw new ArgumentException($"World maps referencing MapDataId {map.MapDataId} must share the same immutable collision content.", nameof(maps));
                }
            }
            else
            {
                sharedCollisions.Add(map.MapDataId, map.Collision);
            }
        }

        if (indexed.Count == 0)
        {
            throw new ArgumentException("A world-map content catalog cannot be empty.", nameof(maps));
        }

        _maps = indexed.ToFrozenDictionary();
    }

    public int Count => _maps.Count;
    public IEnumerable<WorldMapContent> Maps => _maps.Values;

    public bool TryGet(uint mapId, [NotNullWhen(true)] out WorldMapContent? map)
    {
        return _maps.TryGetValue(mapId, out map);
    }
}
