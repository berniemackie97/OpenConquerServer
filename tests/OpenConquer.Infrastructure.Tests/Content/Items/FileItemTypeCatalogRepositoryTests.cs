using OpenConquer.Application.Items.Catalog;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Content.Items;

namespace OpenConquer.Infrastructure.Tests.Content.Items;

public sealed class FileItemTypeCatalogRepositoryTests
{
    private static readonly string s_hash = new('a', 64);

    [Fact]
    public async Task LoadAsync_ValidContent_ReturnsCatalog()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First", stackCapacity: 0),
            Entry(100002, "Second", stackCapacity: 20)));
        ItemTypeCatalog catalog = await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken);
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
        Assert.Equal(55, first.RequiredProfessionCode);
        Assert.Equal(1024u, first.MonopolyFlags);
        Assert.Equal(50_000u, first.SilverPrice);
        Assert.Equal(5, first.AttackRange);
        Assert.Equal(900, first.AttackSpeed);
        Assert.Equal(300u, first.ConquerPointsPrice);
        Assert.True(catalog.TryGet(100002, out ItemTypeDefinition? second));
        Assert.Equal((ushort)20, second.EffectiveStackCapacity);
        Assert.True(second.IsStackable);
    }

    [Fact]
    public async Task LoadAsync_MaximumRepresentableStaticLifetime_ReturnsCatalog()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First",
            staticLifetimeMinutes: ItemTypeDefinition.MaximumStaticLifetimeMinutes)));
        ItemTypeCatalog catalog = await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(catalog.TryGet(100001, out ItemTypeDefinition? item));
        Assert.Equal(ItemTypeDefinition.MaximumStaticLifetimeMinutes, item.StaticLifetimeMinutes);
        Assert.Equal(2_147_483_640, item.StaticLifetimeDurationSeconds);
    }

    [Fact]
    public async Task LoadAsync_StaticLifetimeAboveRepresentableMaximum_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First",
            staticLifetimeMinutes: ItemTypeDefinition.MaximumStaticLifetimeMinutes + 1)));
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("contains invalid state", e.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(e.InnerException);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedFormatVersion_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new("""{"formatVersion":3,"sourceSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","itemTypes":[]}""");
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("format version 3 is unsupported", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_OldFormatVersion_IsRejected()
    {
        using TemporaryFile file = new("""{"formatVersion":1,"itemTypes":[]}""");
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("malformed", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task LoadAsync_InvalidSourceHash_ThrowsInvalidDataException(string hash)
    {
        using TemporaryFile file = new($$"""{"formatVersion":2,"sourceSha256":"{{hash}}","itemTypes":[]}""");
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("source SHA-256", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnknownProperty_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new($$"""{"formatVersion":2,"sourceSha256":"{{s_hash}}","itemTypes":[],"unexpected":true}""");
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_MissingNewNativeProperty_ThrowsInvalidDataException()
    {
        string incomplete = Entry(100001, "Incomplete").Replace("\"monopolyFlags\": 1024,", string.Empty, StringComparison.Ordinal);
        using TemporaryFile file = new(Catalog(incomplete));
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("malformed", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_EmptyCatalog_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog());
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_DuplicateItemType_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First"), Entry(100001, "Duplicate")));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_InvalidItemType_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog(Entry(0, "Invalid")));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await Repository(file.Path).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_TooManyDefinitions_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First"), Entry(100002, "Second")));
        FileItemTypeCatalogRepository repo = new(new ItemTypeCatalogFileOptions(file.Path, 1024 * 1024, 1));
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repo.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("exceeding the configured maximum of 1", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_FileExceedsMaximumLength_ThrowsInvalidDataException()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First")));
        FileItemTypeCatalogRepository repo = new(new ItemTypeCatalogFileOptions(file.Path, 1, 100));
        InvalidDataException e = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repo.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("exceeds the configured maximum", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await Repository(path).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_ThrowsOperationCanceledException()
    {
        using TemporaryFile file = new(Catalog(Entry(100001, "First")));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Repository(file.Path).LoadAsync(cancellation.Token));
    }

    private static FileItemTypeCatalogRepository Repository(string path) =>
        new(new ItemTypeCatalogFileOptions(path, 1024 * 1024, 100));

    private static string Catalog(params string[] entries) => $$"""
        {
          "formatVersion": 2,
          "sourceSha256": "{{s_hash}}",
          "itemTypes": [
            {{string.Join(",\n", entries)}}
          ]
        }
        """;

    private static string Entry(uint id, string name, ushort stackCapacity = 1, uint staticLifetimeMinutes = 10080) => $$"""
        {
          "itemTypeId": {{id}},
          "name": "{{name}}",
          "requiredLevel": 15,
          "speedPercentOffset": -5,
          "life": 120,
          "mana": 30,
          "initialDurability": 100,
          "maximumDurability": 200,
          "staticLifetimeMinutes": {{staticLifetimeMinutes}},
          "stackCapacity": {{stackCapacity}},
          "requiredProfessionCode": 55,
          "monopolyFlags": 1024,
          "silverPrice": 50000,
          "attackRange": 5,
          "attackSpeed": 900,
          "conquerPointsPrice": 300
        }
        """;

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
