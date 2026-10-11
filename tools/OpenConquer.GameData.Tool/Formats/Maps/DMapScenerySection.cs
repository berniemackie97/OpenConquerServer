using System.Collections.Immutable;

namespace OpenConquer.GameData.Tool.Formats.Maps;

internal sealed class DMapScenerySection
{
    internal DMapScenerySection(int subcomponentCount, ImmutableArray<DMapTerrainObjectGroup> terrainObjectGroups)
    {
        SubcomponentCount = subcomponentCount;
        TerrainObjectGroups = terrainObjectGroups;
    }

    public int SubcomponentCount { get; }
    public ImmutableArray<DMapTerrainObjectGroup> TerrainObjectGroups { get; }
}
