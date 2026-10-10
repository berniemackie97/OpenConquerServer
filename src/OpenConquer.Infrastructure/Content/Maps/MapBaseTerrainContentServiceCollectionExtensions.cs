using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public static class MapBaseTerrainContentServiceCollectionExtensions
{
    public static IServiceCollection AddMapBaseTerrainContent(this IServiceCollection services, string rootPath, MapTerrainLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(limits);

        services.AddSingleton<IMapBaseTerrainCatalogRepository>(_ => new FileMapBaseTerrainCatalogRepository(rootPath, limits));

        return services;
    }
}
