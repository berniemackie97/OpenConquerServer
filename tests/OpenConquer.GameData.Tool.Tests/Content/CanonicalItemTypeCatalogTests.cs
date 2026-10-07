using System.Security.Cryptography;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class CanonicalItemTypeCatalogTests
{
    private const int ExpectedDefinitionCount = 14_981;
    private const string ExpectedCatalogSha256 = "e18b1822dd9270c4c5fd36c7c81c2c413c9cd9e3cf73da9a50f18856bb484ab7";

    [Fact]
    public async Task Clean5517Catalog_HasExpectedIdentityAndLoadsSuccessfully()
    {
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "Content", "Items", "item-types.json");
        byte[] payload = await File.ReadAllBytesAsync(catalogPath, TestContext.Current.CancellationToken);

        string catalogSha256 = Convert.ToHexStringLower(SHA256.HashData(payload));
        Assert.Equal(ExpectedCatalogSha256, catalogSha256);

        FileItemTypeCatalogRepository repository = new(new ItemTypeCatalogFileOptions(
            catalogPath,
            maximumFileLengthBytes: 8 * 1024 * 1024,
            maximumDefinitions: 20_000));

        ItemTypeCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedDefinitionCount, catalog.Count);
    }
}
