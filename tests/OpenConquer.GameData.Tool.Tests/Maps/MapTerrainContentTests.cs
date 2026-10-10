using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapTerrainContentTests
{
    [Fact]
    public async Task ReleasedTerrainCatalog_LoadsEveryRequiredArtifact()
    {
        string contentDirectory = FindContentDirectory();
        string definitionsPath = Path.Combine(contentDirectory, "map-definitions.json");
        string terrainDirectory = Path.Combine(contentDirectory, "terrains");

        FileMapDefinitionCatalogRepository definitionsRepository = new(
            new MapDefinitionCatalogFileOptions(definitionsPath, 1024 * 1024, 10_000));

        MapDefinitionCatalog definitions = await definitionsRepository.LoadAsync(TestContext.Current.CancellationToken);

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        string[] expectedFiles = requiredIds.Select(id => $"{id}.ocbt")
            .Order(StringComparer.Ordinal).ToArray();

        string[] actualFiles = Directory.EnumerateFiles(terrainDirectory, "*.ocbt", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedFiles, actualFiles);

        FileMapBaseTerrainCatalogRepository terrainRepository = new(
            terrainDirectory, MapTerrainLoadLimits.CreateDefault());

        MapBaseTerrainCatalog terrains = await terrainRepository.LoadAsync(definitions,
            TestContext.Current.CancellationToken);

        Assert.Equal(requiredIds.Length, terrains.Count);

        foreach (uint mapDataId in requiredIds)
        {
            Assert.True(terrains.TryGet(mapDataId, out MapBaseTerrain? terrain));
            Assert.Equal(mapDataId, terrain.MapDataId);
            Assert.Equal((long)terrain.Width * terrain.Height, terrain.CellCount);
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
