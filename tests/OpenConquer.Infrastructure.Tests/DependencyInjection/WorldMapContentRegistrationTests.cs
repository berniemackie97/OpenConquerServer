using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;
using OpenConquer.Infrastructure.Content.Maps;
using OpenConquer.Infrastructure.DependencyInjection;

namespace OpenConquer.Infrastructure.Tests.DependencyInjection;

public sealed class WorldMapContentRegistrationTests
{
    [Fact]
    public void AddWorldMapContent_RegistersCompleteLoadingPipeline()
    {
        ServiceCollection services = new();
        MapDefinitionCatalogFileOptions options = CreateOptions();
        MapTerrainLoadLimits terrainLimits = MapTerrainLoadLimits.CreateDefault();
        MapCollisionLoadLimits collisionLimits = MapCollisionLoadLimits.CreateDefault();

        IServiceCollection registered = services.AddWorldMapContent(
            options,
            "terrains",
            terrainLimits,
            "collision",
            collisionLimits
        );

        Assert.Same(services, registered);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<FileMapDefinitionCatalogRepository>(
            provider.GetRequiredService<IMapDefinitionCatalogRepository>()
        );
        Assert.IsType<FileMapBaseTerrainCatalogRepository>(
            provider.GetRequiredService<IMapBaseTerrainCatalogRepository>()
        );
        Assert.IsType<FileMapTerrainCollisionCatalogRepository>(
            provider.GetRequiredService<IMapTerrainCollisionCatalogRepository>()
        );

        WorldMapContentLoader first = provider.GetRequiredService<WorldMapContentLoader>();
        WorldMapContentLoader second = provider.GetRequiredService<WorldMapContentLoader>();

        Assert.Same(first, second);
        Assert.Same(options, provider.GetRequiredService<MapDefinitionCatalogFileOptions>());
    }

    [Fact]
    public void AddWorldMapContent_InvalidArguments_AreRejected()
    {
        ServiceCollection services = new();
        MapDefinitionCatalogFileOptions options = CreateOptions();
        MapTerrainLoadLimits terrainLimits = MapTerrainLoadLimits.CreateDefault();
        MapCollisionLoadLimits collisionLimits = MapCollisionLoadLimits.CreateDefault();

        Assert.Throws<ArgumentNullException>(() =>
            WorldMapContentServiceCollectionExtensions.AddWorldMapContent(
                null!,
                options,
                "terrains",
                terrainLimits,
                "collision",
                collisionLimits
            )
        );
        Assert.Throws<ArgumentNullException>(() =>
            services.AddWorldMapContent(
                null!,
                "terrains",
                terrainLimits,
                "collision",
                collisionLimits
            )
        );
        Assert.Throws<ArgumentException>(() =>
            services.AddWorldMapContent(options, " ", terrainLimits, "collision", collisionLimits)
        );
        Assert.Throws<ArgumentNullException>(() =>
            services.AddWorldMapContent(options, "terrains", null!, "collision", collisionLimits)
        );
        Assert.Throws<ArgumentException>(() =>
            services.AddWorldMapContent(options, "terrains", terrainLimits, " ", collisionLimits)
        );
        Assert.Throws<ArgumentNullException>(() =>
            services.AddWorldMapContent(options, "terrains", terrainLimits, "collision", null!)
        );
    }

    private static MapDefinitionCatalogFileOptions CreateOptions()
    {
        return new MapDefinitionCatalogFileOptions("map-definitions.json", 1024 * 1024, 10_000);
    }
}
