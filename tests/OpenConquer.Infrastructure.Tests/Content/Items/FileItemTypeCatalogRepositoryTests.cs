using OpenConquer.Application.Items.Catalog;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.Tests.Content.Items;

public sealed class FileItemTypeCatalogRepositoryTests
{
    [Fact]
    public async Task LoadAsync_ValidContent_ReturnsCatalog()
    {
        using TemporaryFile file = new(CreateCatalog(
            CreateEntry(100001, "First", stackCapacity: 0),
            CreateEntry(100002, "Second", stackCapacity: 20)));

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        ItemTypeCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);

        Assert.True(catalog.TryGet(100001, out ItemTypeDefinition? first));
        Assert.Equal("First", first.Name);
        Assert.Equal((byte)15, first.RequiredLevel);
        Assert.Equal((short)-5, first.SpeedPercentOffset);
        Assert.Equal((short)120, first.Life);
        Assert.Equal((short)30, first.Mana);
        Assert.Equal((ushort)100, first.InitialDurability);
        Assert.Equal((ushort)200, first.MaximumDurability);
        Assert.Equal(10080u, first.StaticLifetimeMinutes);
        Assert.Equal((ushort)0, first.StackCapacity);
        Assert.Equal((ushort)1, first.EffectiveStackCapacity);
        Assert.False(first.IsStackable);

        Assert.True(catalog.TryGet(100002, out ItemTypeDefinition? second));
        Assert.Equal((ushort)20, second.StackCapacity);
        Assert.Equal((ushort)20, second.EffectiveStackCapacity);
        Assert.True(second.IsStackable);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedFormatVersion_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(
            """
            {
              "formatVersion": 2,
              "itemTypes": []
            }
            """);

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("format version 2 is unsupported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnknownProperty_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(
            """
            {
              "formatVersion": 1,
              "itemTypes": [],
              "unexpected": true
            }
            """);

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_EmptyCatalog_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(
            """
            {
              "formatVersion": 1,
              "itemTypes": []
            }
            """);

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_DuplicateItemType_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(CreateCatalog(
            CreateEntry(100001, "First"),
            CreateEntry(100001, "Duplicate")));

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_InvalidItemType_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(CreateCatalog(CreateEntry(0, "Invalid")));

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_TooManyDefinitions_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(CreateCatalog(
            CreateEntry(100001, "First"),
            CreateEntry(100002, "Second")));

        FileItemTypeCatalogRepository repository = new(
            new ItemTypeCatalogFileOptions(file.Path, maximumFileLengthBytes: 1024 * 1024, maximumDefinitions: 1));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exceeding the configured maximum of 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_FileExceedsMaximumLength_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(CreateCatalog(CreateEntry(100001, "First")));

        FileItemTypeCatalogRepository repository = new(
            new ItemTypeCatalogFileOptions(file.Path, maximumFileLengthBytes: 1, maximumDefinitions: 100));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exceeds the configured maximum", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        FileItemTypeCatalogRepository repository = CreateRepository(filePath);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_ThrowsOperationCanceledException()
    {
        using TemporaryFile file = new(CreateCatalog(CreateEntry(100001, "First")));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        FileItemTypeCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.LoadAsync(cancellation.Token));
    }

    private static FileItemTypeCatalogRepository CreateRepository(string filePath)
    {
        return new FileItemTypeCatalogRepository(
            new ItemTypeCatalogFileOptions(filePath, maximumFileLengthBytes: 1024 * 1024, maximumDefinitions: 100));
    }

    private static string CreateCatalog(params string[] entries)
    {
        return $$"""
            {
              "formatVersion": 1,
              "itemTypes": [
                {{string.Join(",\n", entries)}}
              ]
            }
            """;
    }

    private static string CreateEntry(uint itemTypeId, string name, ushort stackCapacity = 1)
    {
        return $$"""
                {
                  "itemTypeId": {{itemTypeId}},
                  "name": "{{name}}",
                  "requiredLevel": 15,
                  "speedPercentOffset": -5,
                  "life": 120,
                  "mana": 30,
                  "initialDurability": 100,
                  "maximumDurability": 200,
                  "staticLifetimeMinutes": 10080,
                  "stackCapacity": {{stackCapacity}}
                }
            """;
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
