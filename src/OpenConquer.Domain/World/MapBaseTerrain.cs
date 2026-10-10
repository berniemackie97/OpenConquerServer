using System.Collections.Immutable;

namespace OpenConquer.Domain.World;

/// <summary>
/// One verified DMap base-grid cell. PassabilityFlag is source data, not final authoritative collision.
/// </summary>
public readonly record struct MapBaseTerrainCell(ushort SurfaceId, ushort PassabilityFlag, short Elevation);

/// <summary>
/// One native ground-layer exit marker, including markers outside the base grid.
/// </summary>
public readonly record struct MapTerrainExit(int X, int Y, uint PasswayIndex);

/// <summary>
/// Immutable base terrain identified by MapDataId. Does not include scenery collision composition.
/// </summary>
public sealed class MapBaseTerrain
{
    private readonly ImmutableArray<MapBaseTerrainCell> _cells;
    private readonly ImmutableArray<MapTerrainExit> _exits;

    public MapBaseTerrain(uint mapDataId, int width, int height, IEnumerable<MapBaseTerrainCell> cells, IEnumerable<MapTerrainExit> exits)
    {
        if (mapDataId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapDataId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(exits);

        long expectedCellCount = (long)width * height;

        if (expectedCellCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Terrain dimensions exceed the supported cell count.");
        }

        ImmutableArray<MapBaseTerrainCell> immutableCells = [.. cells];

        if (immutableCells.Length != expectedCellCount)
        {
            throw new ArgumentException($"Terrain requires {expectedCellCount} cells, received {immutableCells.Length}.", nameof(cells));
        }

        MapDataId = mapDataId;
        Width = width;
        Height = height;
        _cells = immutableCells;
        _exits = [.. exits];
    }

    public uint MapDataId { get; }
    public int Width { get; }
    public int Height { get; }
    public int CellCount => _cells.Length;
    public ImmutableArray<MapBaseTerrainCell> Cells => _cells;
    public ImmutableArray<MapTerrainExit> Exits => _exits;

    public MapBaseTerrainCell GetCell(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);

        if (x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return _cells[(y * Width) + x];
    }
}
