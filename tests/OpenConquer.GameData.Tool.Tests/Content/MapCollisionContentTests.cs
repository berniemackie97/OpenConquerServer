using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class MapCollisionContentTests
{
    [Fact]
    public async Task ReleasedCollisionCatalog_LoadsExactlyEveryRequiredArtifact()
    {
        string contentDirectory = FindContentDirectory();
        string definitionsPath = Path.Combine(contentDirectory, "map-definitions.json");
        string terrainDirectory = Path.Combine(contentDirectory, "terrains");
        string collisionDirectory = Path.Combine(contentDirectory, "collision");

        FileMapDefinitionCatalogRepository definitionsRepository = new(
            new MapDefinitionCatalogFileOptions(definitionsPath, 1024 * 1024, 10_000));

        MapDefinitionCatalog definitions = await definitionsRepository.LoadAsync(TestContext.Current.CancellationToken);

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId)
            .Distinct().Order().ToArray();

        string[] expectedFiles = requiredIds.Select(id => $"{id}.ocmc")
            .Order(StringComparer.Ordinal).ToArray();

        string[] actualFiles = Directory.EnumerateFiles(collisionDirectory, "*.ocmc", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedFiles, actualFiles);

        FileMapBaseTerrainCatalogRepository terrainRepository = new(
            terrainDirectory, MapTerrainLoadLimits.CreateDefault());

        MapBaseTerrainCatalog terrains = await terrainRepository.LoadAsync(
            definitions, TestContext.Current.CancellationToken);

        MapCollisionLoadLimits limits = MapCollisionLoadLimits.CreateDefault();
        long totalOverrides = 0;

        foreach (uint mapDataId in requiredIds)
        {
            Assert.True(terrains.TryGet(mapDataId, out MapBaseTerrain? terrain));

            string path = Path.Combine(collisionDirectory, $"{mapDataId}.ocmc");

            MapTerrainCollision collision = await MapCollisionBinaryReader.ReadAsync(
                path, terrain, limits, TestContext.Current.CancellationToken);

            Assert.Equal(mapDataId, collision.MapDataId);
            Assert.Equal(terrain.Width, collision.Width);
            Assert.Equal(terrain.Height, collision.Height);

            totalOverrides = checked(totalOverrides + collision.OverrideCount);

            Assert.True(totalOverrides <= limits.MaximumTotalOverrides);
        }
    }

    [Fact]
    public async Task ReleasedCollision_ResearchPasswaySamples_MatchEffectivePassability()
    {
        string contentDirectory = FindContentDirectory();
        string definitionsPath = Path.Combine(contentDirectory, "map-definitions.json");
        string terrainDirectory = Path.Combine(contentDirectory, "terrains");
        string collisionDirectory = Path.Combine(contentDirectory, "collision");

        FileMapDefinitionCatalogRepository definitionsRepository = new(
            new MapDefinitionCatalogFileOptions(definitionsPath, 1024 * 1024, 10_000));

        MapDefinitionCatalog definitions = await definitionsRepository.LoadAsync(TestContext.Current.CancellationToken);

        FileMapBaseTerrainCatalogRepository terrainRepository = new(
            terrainDirectory, MapTerrainLoadLimits.CreateDefault());

        MapBaseTerrainCatalog terrains = await terrainRepository.LoadAsync(
            definitions, TestContext.Current.CancellationToken);

        // Portal destination coordinates with known walkability. These are spot checks
        // derived independently of the collision generator, not a full tile comparison.
        (uint MapDataId, int X, int Y, bool Walkable)[] samples =
        [
            (1000, 159, 467, false),
            (1015, 511, 533, false),
            (1001, 312, 646, true),
            (1002, 69, 473, true),
            (1015, 1008, 707, true)
        ];

        Dictionary<uint, MapTerrainCollision> loaded = [];

        foreach ((uint mapDataId, int x, int y, bool expectedWalkable) in samples)
        {
            if (!loaded.TryGetValue(mapDataId, out MapTerrainCollision? collision))
            {
                Assert.True(terrains.TryGet(mapDataId, out MapBaseTerrain? baseTerrain));

                collision = await MapCollisionBinaryReader.ReadAsync(
                    Path.Combine(collisionDirectory, $"{mapDataId}.ocmc"),
                    baseTerrain, MapCollisionLoadLimits.CreateDefault(),
                    TestContext.Current.CancellationToken);

                loaded.Add(mapDataId, collision);
            }

            bool walkable = collision.GetEffectiveCell(x, y).PassabilityFlag == 0;

            Assert.Equal(expectedWalkable, walkable);
        }
    }

    private static string FindContentDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "content", "maps");

            if (File.Exists(Path.Combine(candidate, "map-definitions.json"))
                && Directory.Exists(Path.Combine(candidate, "terrains")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Repository map content could not be located.");
    }
}
