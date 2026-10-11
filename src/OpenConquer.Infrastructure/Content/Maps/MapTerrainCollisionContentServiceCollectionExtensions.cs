using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public static class MapTerrainCollisionContentServiceCollectionExtensions
{
    public static IServiceCollection AddMapTerrainCollisionContent(this IServiceCollection services, string rootPath, MapCollisionLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(limits);

        services.AddSingleton<IMapTerrainCollisionCatalogRepository>(_ => new FileMapTerrainCollisionCatalogRepository(rootPath, limits));

        return services;
    }
}
