using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Items.Catalog;

namespace OpenConquer.Infrastructure.Content.Items;

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
