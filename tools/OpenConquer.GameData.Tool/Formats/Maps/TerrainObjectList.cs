using System.Collections.Immutable;

namespace OpenConquer.GameData.Tool.Formats.Maps;

internal sealed class TerrainObjectList
{
    internal TerrainObjectList(ImmutableArray<TerrainObjectEntry> entries)
    {
        Entries = entries;
    }

    public ImmutableArray<TerrainObjectEntry> Entries { get; }
}
