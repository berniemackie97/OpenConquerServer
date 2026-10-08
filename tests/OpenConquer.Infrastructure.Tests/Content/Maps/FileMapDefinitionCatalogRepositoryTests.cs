using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.Infrastructure.Tests.Content.Maps;

public sealed class FileMapDefinitionCatalogRepositoryTests
{
    [Fact]
    public async Task LoadAsync_ValidCatalog_PreservesWorldIdentityAnd64BitFlags()
    {
        using TemporaryFile file = new(
            """
            {
              "formatVersion": 1,
              "maps": [
                {"mapId": 1002, "mapDataId": 1010, "flags": 274877906960},
                {"mapId": 1003, "mapDataId": 1010, "flags": 18446744073709551615}
              ]
            }
            """);

        FileMapDefinitionCatalogRepository repository = CreateRepository(file.Path);
        MapDefinitionCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);

        Assert.True(catalog.TryGet(1002, out MapDefinition? first));
        Assert.Equal(1010u, first.MapDataId);
        Assert.Equal(274877906960ul, first.Flags);

        Assert.True(catalog.TryGet(1003, out MapDefinition? second));
        Assert.Equal(1010u, second.MapDataId);
        Assert.Equal(ulong.MaxValue, second.Flags);
    }

    [Theory]
    [InlineData("""{"formatVersion":2,"maps":[{"mapId":1,"mapDataId":2,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[]}""")]
    [InlineData("""{"formatVersion":1,"maps":null}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":0,"mapDataId":2,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":0,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":-1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":18446744073709551616}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"unexpected":true}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"flags":1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0},{"mapId":1,"mapDataId":3,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[null]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0}],"unexpected":true}""")]
    [InlineData("""{"formatVersion":1,"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,}]}""")]
    public async Task LoadAsync_InvalidContent_FailsClosed(string content)
    {
        using TemporaryFile file = new(content);
        FileMapDefinitionCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_TooManyDefinitions_IsRejected()
    {
        using TemporaryFile file = new(
            """
            {"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0},{"mapId":3,"mapDataId":2,"flags":0}]}
            """);

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(file.Path, 1024 * 1024, maximumDefinitions: 1));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_OversizedFile_IsRejected()
    {
        using TemporaryFile file = new(
            """
            {"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0}]}
            """);

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(file.Path, maximumFileLengthBytes: 1, maximumDefinitions: 100));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_MissingFile_IsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        FileMapDefinitionCatalogRepository repository = CreateRepository(path);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await repository.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_DoesNotLoadContent()
    {
        using TemporaryFile file = new(
            """
            {"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0}]}
            """);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        FileMapDefinitionCatalogRepository repository = CreateRepository(file.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await repository.LoadAsync(cancellation.Token));
    }

    [Fact]
    public void Options_InvalidLimits_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new MapDefinitionCatalogFileOptions(" ", 1024, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapDefinitionCatalogFileOptions("maps.json", 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapDefinitionCatalogFileOptions("maps.json", 1024, 0));
    }

    private static FileMapDefinitionCatalogRepository CreateRepository(string path)
    {
        return new FileMapDefinitionCatalogRepository(
            new MapDefinitionCatalogFileOptions(path, maximumFileLengthBytes: 1024 * 1024, maximumDefinitions: 100));
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
