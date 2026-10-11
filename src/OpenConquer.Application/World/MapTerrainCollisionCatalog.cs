using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class MapTerrainCollisionCatalog
{
    private readonly FrozenDictionary<uint, MapTerrainCollision> _collisions;

    public MapTerrainCollisionCatalog(IEnumerable<MapTerrainCollision> collisions)
    {
        ArgumentNullException.ThrowIfNull(collisions);

        Dictionary<uint, MapTerrainCollision> indexed = [];

        foreach (MapTerrainCollision collision in collisions)
        {
            if (collision is null)
            {
                throw new ArgumentException("A collision catalog cannot contain a null collision.", nameof(collisions));
            }

            if (!indexed.TryAdd(collision.MapDataId, collision))
            {
                throw new ArgumentException($"Collision catalog contains duplicate MapDataId {collision.MapDataId}.", nameof(collisions));
            }
        }

        if (indexed.Count == 0)
        {
            throw new ArgumentException("A collision catalog cannot be empty.", nameof(collisions));
        }

        _collisions = indexed.ToFrozenDictionary();
    }

    public int Count => _collisions.Count;

    public bool TryGet(uint mapDataId, [NotNullWhen(true)] out MapTerrainCollision? collision)
    {
        return _collisions.TryGetValue(mapDataId, out collision);
    }
}
