using System.Collections.Immutable;

namespace OpenConquer.GameData.Tool.Formats.Maps;

internal readonly record struct TerrainObjectEntry(int Width, int Height, int AnchorTileOffsetX, int AnchorTileOffsetY, ImmutableArray<TerrainObjectCell> Cells);
