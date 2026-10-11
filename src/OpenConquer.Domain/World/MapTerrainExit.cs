namespace OpenConquer.Domain.World;

/// <summary>
/// One ground-layer exit marker, including markers outside the base grid.
/// </summary>
public readonly record struct MapTerrainExit(int X, int Y, uint PasswayIndex);
