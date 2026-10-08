using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;

namespace OpenConquer.Infrastructure.Content.Maps;

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
