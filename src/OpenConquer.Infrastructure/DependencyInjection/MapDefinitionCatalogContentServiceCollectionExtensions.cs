using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class MapDefinitionCatalogContentServiceCollectionExtensions
{
    public static IServiceCollection AddMapDefinitionCatalogContent(this IServiceCollection services, MapDefinitionCatalogFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IMapDefinitionCatalogRepository, FileMapDefinitionCatalogRepository>();

        return services;
    }
}
