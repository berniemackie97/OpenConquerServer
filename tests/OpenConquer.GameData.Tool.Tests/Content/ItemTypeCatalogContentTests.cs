using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class ItemTypeCatalogContentTests
{
    private const int ExpectedDefinitionCount = 14_981;
    private const string ExpectedSourceSha256 = "90e9c983b0f78337f20720a3cf61d8f1392f23979565da14eb01dc514e3a71e7";
    private const string ExpectedCatalogSha256 = "5862d91e68b408942be81512877a7bdb818d3f5f7106a593668222458d89246c";

    [Fact]
    public async Task ReleasedCatalog_HasExpectedIdentityAndLoadsSuccessfully()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Content", "Items", "item-types.json");
        byte[] payload = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(ExpectedCatalogSha256, Convert.ToHexStringLower(SHA256.HashData(payload)));

        using JsonDocument json = JsonDocument.Parse(payload);
        Assert.Equal(2, json.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(ExpectedSourceSha256, json.RootElement.GetProperty("sourceSha256").GetString());
        Assert.Equal(ExpectedDefinitionCount, json.RootElement.GetProperty("itemTypes").GetArrayLength());

        FileItemTypeCatalogRepository repository = new(new ItemTypeCatalogFileOptions(
            path, maximumFileLengthBytes: 16 * 1024 * 1024, maximumDefinitions: 20_000));
        ItemTypeCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ExpectedDefinitionCount, catalog.Count);
    }
}
