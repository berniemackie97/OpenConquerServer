namespace OpenConquer.Domain.World;

public readonly record struct MapTerrainAttachment(int X, int Y, ushort SurfaceId, ushort PassabilityFlag, short ElevationDelta);
