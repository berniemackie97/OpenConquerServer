using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class ItemTypeCatalogContentServiceCollectionExtensions
{
    public static IServiceCollection AddItemTypeCatalogContent(this IServiceCollection services, ItemTypeCatalogFileOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IItemTypeCatalogRepository, FileItemTypeCatalogRepository>();

        return services;
    }
}
