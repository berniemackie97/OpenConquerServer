using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class MapTerrainCollisionCatalogContentTests
{
    [Fact]
    public async Task ReleasedCollisionContent_LoadsThroughProductionCatalogRepository()
    {
        string contentDirectory = FindContentDirectory();

        FileMapDefinitionCatalogRepository definitionsRepository = new(
            new MapDefinitionCatalogFileOptions(Path.Combine(contentDirectory, "map-definitions.json"), 1024 * 1024, 10_000));

        MapDefinitionCatalog definitions = await definitionsRepository.LoadAsync(TestContext.Current.CancellationToken);

        FileMapBaseTerrainCatalogRepository terrainRepository = new(
            Path.Combine(contentDirectory, "terrains"), MapTerrainLoadLimits.CreateDefault());

        MapBaseTerrainCatalog terrains = await terrainRepository.LoadAsync(definitions, TestContext.Current.CancellationToken);

        FileMapTerrainCollisionCatalogRepository collisionRepository = new(
            Path.Combine(contentDirectory, "collision"), MapCollisionLoadLimits.CreateDefault());

        MapTerrainCollisionCatalog collisions = await collisionRepository.LoadAsync(definitions, terrains, TestContext.Current.CancellationToken);

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        Assert.Equal(requiredIds.Length, terrains.Count);
        Assert.Equal(requiredIds.Length, collisions.Count);

        long totalOverrides = 0;
        int emptyCount = 0;

        foreach (uint mapDataId in requiredIds)
        {
            Assert.True(terrains.TryGet(mapDataId, out MapBaseTerrain? terrain));
            Assert.True(collisions.TryGet(mapDataId, out MapTerrainCollision? collision));

            Assert.Same(terrain, collision.BaseTerrain);
            Assert.Equal(mapDataId, collision.MapDataId);

            totalOverrides = checked(totalOverrides + collision.OverrideCount);

            if (collision.OverrideCount == 0)
            {
                emptyCount++;
            }
        }

        Assert.Equal(20_275, totalOverrides);
        Assert.Equal(156, emptyCount);
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

        throw new DirectoryNotFoundException("Repository canonical map content could not be located.");
    }
}
