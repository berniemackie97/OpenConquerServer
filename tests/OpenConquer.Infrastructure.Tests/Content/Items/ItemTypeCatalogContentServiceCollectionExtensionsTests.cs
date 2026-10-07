using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.Tests.Content.Items;

public sealed class ItemTypeCatalogContentServiceCollectionExtensionsTests
{
    [Fact]
    public void AddItemTypeCatalogContent_RejectsMissingConfiguration()
    {
        ItemTypeCatalogFileOptions options = new("item-types.json", maximumFileLengthBytes: 1024, maximumDefinitions: 100);

        Assert.Throws<ArgumentNullException>(() =>
            ItemTypeCatalogContentServiceCollectionExtensions.AddItemTypeCatalogContent(null!, options));

        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddItemTypeCatalogContent(null!));
    }

    [Fact]
    public void AddItemTypeCatalogContent_RegistersSingletonRepositoryAndOptions()
    {
        ItemTypeCatalogFileOptions options = new("item-types.json", maximumFileLengthBytes: 1024, maximumDefinitions: 100);

        using ServiceProvider services = new ServiceCollection()
            .AddItemTypeCatalogContent(options)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(options, services.GetRequiredService<ItemTypeCatalogFileOptions>());

        IItemTypeCatalogRepository first = services.GetRequiredService<IItemTypeCatalogRepository>();
        IItemTypeCatalogRepository second = services.GetRequiredService<IItemTypeCatalogRepository>();

        Assert.IsType<FileItemTypeCatalogRepository>(first);
        Assert.Same(first, second);
    }
}
