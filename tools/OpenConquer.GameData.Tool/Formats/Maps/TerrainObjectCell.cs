namespace OpenConquer.GameData.Tool.Formats.Maps;

internal readonly record struct TerrainObjectCell(ushort PassabilityFlag, ushort SurfaceId, short ElevationDelta);
