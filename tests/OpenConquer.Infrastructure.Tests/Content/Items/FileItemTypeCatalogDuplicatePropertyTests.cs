using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.Tests.Content.Items;

public sealed class FileItemTypeCatalogDuplicatePropertyTests
{
    [Theory]
    [InlineData("""{"formatVersion":2,"formatVersion":3,"itemTypes":[]}""")]
    [InlineData("""{"formatVersion":2,"itemTypes":[],"itemTypes":[]}""")]
    [InlineData("""{"formatVersion":2,"itemTypes":[{"itemTypeId":100001,"itemTypeId":100002}]}""")]
    [InlineData("""{"formatVersion":2,"itemTypes":[{"name":"First","name":"Second"}]}""")]
    public async Task LoadAsync_DuplicateJsonProperties_AreRejected(string content)
    {
        using TemporaryFile file = new(content);
        FileItemTypeCatalogRepository repository = new(
            new ItemTypeCatalogFileOptions(file.Path, maximumFileLengthBytes: 1024 * 1024, maximumDefinitions: 100));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("duplicate JSON property", exception.Message, StringComparison.Ordinal);
    }
}
