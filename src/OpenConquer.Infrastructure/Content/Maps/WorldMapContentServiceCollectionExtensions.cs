using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public static class WorldMapContentServiceCollectionExtensions
{
    public static IServiceCollection AddWorldMapContent(this IServiceCollection services, MapDefinitionCatalogFileOptions definitions,
        string terrainDirectory, MapTerrainLoadLimits terrainLimits, string collisionDirectory, MapCollisionLoadLimits collisionLimits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(terrainDirectory);
        ArgumentNullException.ThrowIfNull(terrainLimits);
        ArgumentException.ThrowIfNullOrWhiteSpace(collisionDirectory);
        ArgumentNullException.ThrowIfNull(collisionLimits);

        services.AddMapDefinitionCatalogContent(definitions);
        services.AddMapBaseTerrainContent(terrainDirectory, terrainLimits);
        services.AddMapTerrainCollisionContent(collisionDirectory, collisionLimits);
        services.AddSingleton<WorldMapContentLoader>();

        return services;
    }
}
