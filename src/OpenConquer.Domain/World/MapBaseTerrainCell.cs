namespace OpenConquer.Domain.World;

/// <summary>
/// One base-grid cell. <c>PassabilityFlag</c> is raw terrain data, not final collision.
/// </summary>
public readonly record struct MapBaseTerrainCell(ushort SurfaceId, ushort PassabilityFlag, short Elevation);
