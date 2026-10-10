using System.Collections.Frozen;
using System.Collections.Immutable;

namespace OpenConquer.Domain.World;

public readonly record struct MapTerrainAttachment(int X, int Y, ushort SurfaceId, ushort PassabilityFlag, short ElevationDelta);

public readonly record struct MapTerrainCollisionOverride(int CellIndex, MapBaseTerrainCell Cell);

public sealed class MapTerrainCollision
{
    private readonly FrozenDictionary<int, MapBaseTerrainCell> _overrides;

    public MapTerrainCollision(MapBaseTerrain baseTerrain, IEnumerable<MapTerrainCollisionOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(baseTerrain);
        ArgumentNullException.ThrowIfNull(overrides);

        Dictionary<int, MapBaseTerrainCell> indexed = [];

        foreach (MapTerrainCollisionOverride entry in overrides)
        {
            if (entry.CellIndex < 0 || entry.CellIndex >= baseTerrain.CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(overrides), $"Collision override index {entry.CellIndex} is outside the terrain.");
            }

            if (!indexed.TryAdd(entry.CellIndex, entry.Cell))
            {
                throw new ArgumentException($"Collision contains duplicate override index {entry.CellIndex}.", nameof(overrides));
            }

            if (entry.Cell == baseTerrain.Cells[entry.CellIndex])
            {
                throw new ArgumentException($"Collision contains a redundant override at index {entry.CellIndex}.", nameof(overrides));
            }
        }

        BaseTerrain = baseTerrain;
        Overrides = [.. indexed.OrderBy(entry => entry.Key)
            .Select(entry => new MapTerrainCollisionOverride(entry.Key, entry.Value))];

        _overrides = indexed.ToFrozenDictionary();
    }

    public MapBaseTerrain BaseTerrain { get; }
    public uint MapDataId => BaseTerrain.MapDataId;
    public int Width => BaseTerrain.Width;
    public int Height => BaseTerrain.Height;
    public int OverrideCount => Overrides.Length;
    public ImmutableArray<MapTerrainCollisionOverride> Overrides { get; }

    public MapBaseTerrainCell GetEffectiveCell(int x, int y)
    {
        MapBaseTerrainCell baseCell = BaseTerrain.GetCell(x, y);
        int index = (y * Width) + x;

        return _overrides.TryGetValue(index, out MapBaseTerrainCell effectiveCell)
            ? effectiveCell
            : baseCell;
    }
}
