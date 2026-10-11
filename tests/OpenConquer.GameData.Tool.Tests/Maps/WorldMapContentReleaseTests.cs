using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class WorldMapContentReleaseTests
{
    [Fact]
    public async Task ReleasedWorldMapContent_ResolvesEveryCanonicalWorldMap()
    {
        string contentDirectory = FindContentDirectory();

        WorldMapContentLoader loader = new(
            new FileMapDefinitionCatalogRepository(new MapDefinitionCatalogFileOptions(
                Path.Combine(contentDirectory, "map-definitions.json"), 1024 * 1024, 10_000)),
            new FileMapBaseTerrainCatalogRepository(
                Path.Combine(contentDirectory, "terrains"), MapTerrainLoadLimits.CreateDefault()),
            new FileMapTerrainCollisionCatalogRepository(
                Path.Combine(contentDirectory, "collision"), MapCollisionLoadLimits.CreateDefault()));

        WorldMapContentCatalog catalog = await loader.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(282, catalog.Count);
        Assert.Equal(185, catalog.Maps.Select(map => map.MapDataId).Distinct().Count());

        Dictionary<uint, MapTerrainCollision> sharedCollisions = [];
        HashSet<uint> observedMapIds = [];

        foreach (WorldMapContent map in catalog.Maps)
        {
            Assert.True(observedMapIds.Add(map.MapId));
            Assert.Equal(map.MapDataId, map.Collision.MapDataId);
            Assert.Same(map.BaseTerrain, map.Collision.BaseTerrain);
            Assert.True(catalog.TryGet(map.MapId, out WorldMapContent? resolved));
            Assert.Same(map, resolved);

            if (sharedCollisions.TryGetValue(map.MapDataId, out MapTerrainCollision? existing))
            {
                Assert.Same(existing, map.Collision);
            }
            else
            {
                sharedCollisions.Add(map.MapDataId, map.Collision);
            }
        }

        Assert.Equal(185, sharedCollisions.Count);

        Assert.True(catalog.TryGet(1501, out WorldMapContent? classPkArena));
        Assert.Equal(1730u, classPkArena.MapDataId);
        Assert.Equal(0x0030_010C_4015_0007UL, classPkArena.Flags);

        foreach (uint unavailableMapId in new uint[] { 1807, 1808, 1809, 1810, 1889, 1951, 2092 })
        {
            Assert.False(catalog.TryGet(unavailableMapId, out _));
        }
    }

    private static string FindContentDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "content", "maps");

            if (File.Exists(Path.Combine(candidate, "map-definitions.json")) &&
                Directory.Exists(Path.Combine(candidate, "terrains")) &&
                Directory.Exists(Path.Combine(candidate, "collision")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Repository canonical world-map content could not be located.");
    }
}
