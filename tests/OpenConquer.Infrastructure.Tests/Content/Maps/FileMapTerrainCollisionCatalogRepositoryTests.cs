using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.Infrastructure.Tests.Content.Maps;

public sealed class FileMapTerrainCollisionCatalogRepositoryTests
{
    private static readonly MapCollisionLoadLimits s_limits = new(maximumOverridesPerTerrain: 100, maximumTotalOverrides: 100, maximumArtifactBytes: 1024 * 1024);

    [Fact]
    public async Task LoadAsync_CompleteContent_PublishesImmutableCatalog()
    {
        using Fixture fixture = new();

        MapTerrainCollisionCatalog catalog = await LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(501, out MapTerrainCollision? first));
        Assert.True(catalog.TryGet(502, out MapTerrainCollision? second));
        Assert.Same(fixture.FirstTerrain, first.BaseTerrain);
        Assert.Same(fixture.SecondTerrain, second.BaseTerrain);
        Assert.Equal(1, first.OverrideCount);
        Assert.Equal(new MapBaseTerrainCell(44, 0, 25), first.GetEffectiveCell(1, 0));
        Assert.Equal(new MapBaseTerrainCell(10, 0, -15), first.GetEffectiveCell(0, 0));
        Assert.Equal(0, second.OverrideCount);
        Assert.Equal(fixture.SecondTerrain.GetCell(1, 0), second.GetEffectiveCell(1, 0));
        Assert.False(catalog.TryGet(1000, out _));
    }

    [Fact]
    public async Task LoadAsync_SharedMapDataId_RequiresOneArtifact()
    {
        using Fixture fixture = new();

        MapTerrainCollisionCatalog catalog = await LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, fixture.Definitions.Count);
        Assert.Equal(2, catalog.Count);
        Assert.True(fixture.Definitions.TryGet(1000, out MapDefinition? first));
        Assert.True(fixture.Definitions.TryGet(1001, out MapDefinition? second));
        Assert.Equal(first.MapDataId, second.MapDataId);
    }

    [Fact]
    public async Task LoadAsync_CompletedSnapshot_DoesNotDependOnSourceFiles()
    {
        using Fixture fixture = new();

        MapTerrainCollisionCatalog catalog = await LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

        File.Delete(fixture.ArtifactPath(501));
        File.Delete(fixture.ArtifactPath(502));

        Assert.True(catalog.TryGet(501, out MapTerrainCollision? collision));
        Assert.Same(fixture.FirstTerrain, collision.BaseTerrain);
        Assert.Equal(new MapBaseTerrainCell(44, 0, 25), collision.GetEffectiveCell(1, 0));
    }

    [Fact]
    public async Task LoadAsync_MissingDirectory_IsRejected()
    {
        using Fixture fixture = new();

        FileMapTerrainCollisionCatalogRepository repository = new(Path.Combine(fixture.WorkspacePath, "missing"), s_limits);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            repository.LoadAsync(fixture.Definitions, fixture.Terrains, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task LoadAsync_MissingRequiredArtifact_IsRejected()
    {
        using Fixture fixture = new();
        File.Delete(fixture.ArtifactPath(502));

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("502.ocmc", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnexpectedArtifact_IsRejected()
    {
        using Fixture fixture = new();
        File.WriteAllBytes(Path.Combine(fixture.RootPath, "999.ocmc"), []);

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_UnexpectedNonArtifactFile_IsRejected()
    {
        using Fixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.RootPath, "unexpected.txt"), "unexpected");

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_UnexpectedSubdirectory_IsRejected()
    {
        using Fixture fixture = new();
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "nested"));

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_NonCanonicalFilenameCase_IsRejected()
    {
        using Fixture fixture = new();

        string original = fixture.ArtifactPath(501);
        byte[] payload = File.ReadAllBytes(original);

        File.Delete(original);
        File.WriteAllBytes(Path.Combine(fixture.RootPath, "501.OCMC"), payload);

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_MissingRequiredBaseTerrain_IsRejected()
    {
        using Fixture fixture = new();
        MapBaseTerrainCatalog incomplete = new([fixture.FirstTerrain]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, terrains: incomplete, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("502", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnexpectedBaseTerrain_IsRejected()
    {
        using Fixture fixture = new();

        MapBaseTerrain extra = CreateTerrain(503);
        MapBaseTerrainCatalog expanded = new([fixture.FirstTerrain, fixture.SecondTerrain, extra]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, terrains: expanded, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("3 identities", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_IncompatibleBaseTerrain_IsRejected()
    {
        using Fixture fixture = new();

        MapBaseTerrain changed = CreateTerrain(501, firstSurface: 99);
        MapBaseTerrainCatalog incompatible = new([changed, fixture.SecondTerrain]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, terrains: incompatible, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("different canonical base-terrain content", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_CorruptedArtifact_IsRejected()
    {
        using Fixture fixture = new();

        string path = fixture.ArtifactPath(501);
        byte[] payload = File.ReadAllBytes(path);
        payload[60] ^= 1;
        File.WriteAllBytes(path, payload);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256 integrity check", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_AggregateOverrideLimit_IsEnforced()
    {
        using Fixture fixture = new();

        fixture.WriteArtifact(502, CreateCollision(fixture.SecondTerrain, withOverride: true));
        MapCollisionLoadLimits limits = new(maximumOverridesPerTerrain: 100, maximumTotalOverrides: 1, maximumArtifactBytes: 1024 * 1024);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, limits: limits, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("configured maximum", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_PerArtifactFileLimit_IsEnforced()
    {
        using Fixture fixture = new();

        MapCollisionLoadLimits limits = new(maximumOverridesPerTerrain: 100, maximumTotalOverrides: 100, maximumArtifactBytes: MapCollisionBinaryFormat.MinimumLength);

        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(fixture, limits: limits, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_DoesNotLoadContent()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoadAsync(fixture, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_SymbolicLinkDirectory_IsRejected()
    {
        using Fixture fixture = new();

        string linkPath = Path.Combine(fixture.WorkspacePath, "collision-link");
        Directory.CreateSymbolicLink(linkPath, fixture.RootPath);

        FileMapTerrainCollisionCatalogRepository repository = new(linkPath, s_limits);

        await Assert.ThrowsAsync<IOException>(() =>
            repository.LoadAsync(fixture.Definitions, fixture.Terrains, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task LoadAsync_SymbolicLinkArtifact_IsRejected()
    {
        using Fixture fixture = new();

        string path = fixture.ArtifactPath(501);
        File.Delete(path);
        File.CreateSymbolicLink(path, fixture.ArtifactPath(502));

        await Assert.ThrowsAsync<IOException>(() => LoadAsync(fixture, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Repository_InvalidConstructionArguments_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FileMapTerrainCollisionCatalogRepository(" ", s_limits));
        Assert.Throws<ArgumentNullException>(() => new FileMapTerrainCollisionCatalogRepository("collision", null!));
    }

    [Fact]
    public async Task LoadAsync_NullCatalogArguments_AreRejected()
    {
        using Fixture fixture = new();
        FileMapTerrainCollisionCatalogRepository repository = fixture.CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repository.LoadAsync(null!, fixture.Terrains, TestContext.Current.CancellationToken).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repository.LoadAsync(fixture.Definitions, null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void DependencyInjection_RegistersCollisionRepository()
    {
        using Fixture fixture = new();

        ServiceCollection services = new();
        services.AddMapTerrainCollisionContent(fixture.RootPath, s_limits);

        using ServiceProvider provider = services.BuildServiceProvider();

        IMapTerrainCollisionCatalogRepository first = provider.GetRequiredService<IMapTerrainCollisionCatalogRepository>();
        IMapTerrainCollisionCatalogRepository second = provider.GetRequiredService<IMapTerrainCollisionCatalogRepository>();

        Assert.IsType<FileMapTerrainCollisionCatalogRepository>(first);
        Assert.Same(first, second);
    }

    private static Task<MapTerrainCollisionCatalog> LoadAsync(Fixture fixture, MapBaseTerrainCatalog? terrains = null,
        MapCollisionLoadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        return fixture.CreateRepository(limits).LoadAsync(fixture.Definitions, terrains ?? fixture.Terrains,
            cancellationToken == default ? TestContext.Current.CancellationToken : cancellationToken).AsTask();
    }

    private static MapBaseTerrain CreateTerrain(uint mapDataId, ushort firstSurface = 10)
    {
        return new MapBaseTerrain(mapDataId, 2, 1, [new MapBaseTerrainCell(firstSurface, 0, -15), new MapBaseTerrainCell(20, 1, 30)],
            [new MapTerrainExit(1, 0, 7)]);
    }

    private static MapTerrainCollision CreateCollision(MapBaseTerrain terrain, bool withOverride)
    {
        return new MapTerrainCollision(terrain, withOverride ? [new MapTerrainCollisionOverride(1, new MapBaseTerrainCell(44, 0, 25))] : []);
    }

    private static void WriteArtifact(string path, MapTerrainCollision collision)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write("OCMC"u8);
        writer.Write(MapCollisionBinaryFormat.FormatVersion);
        writer.Write((ushort)0);
        writer.Write(collision.MapDataId);
        writer.Write(checked((uint)collision.Width));
        writer.Write(checked((uint)collision.Height));
        writer.Write(checked((uint)collision.OverrideCount));
        writer.Write(MapBaseTerrainFingerprint.Compute(collision.BaseTerrain));

        foreach (MapTerrainCollisionOverride entry in collision.Overrides)
        {
            writer.Write(checked((uint)entry.CellIndex));
            writer.Write(entry.Cell.SurfaceId);
            writer.Write(entry.Cell.PassabilityFlag);
            writer.Write(entry.Cell.Elevation);
        }

        writer.Flush();
        stream.Position = 0;

        byte[] checksum = SHA256.HashData(stream);

        stream.Position = stream.Length;
        writer.Write(checksum);
        writer.Flush();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            WorkspacePath = Path.Combine(Path.GetTempPath(), $"openconquer-collision-catalog-{Guid.NewGuid():N}");
            RootPath = Path.Combine(WorkspacePath, "collision");
            Directory.CreateDirectory(RootPath);

            FirstTerrain = CreateTerrain(501);
            SecondTerrain = CreateTerrain(502);

            Definitions = new MapDefinitionCatalog([
                new MapDefinition(1000, 501, 0),
                new MapDefinition(1001, 501, 1),
                new MapDefinition(1002, 502, 0)
            ]);

            Terrains = new MapBaseTerrainCatalog([FirstTerrain, SecondTerrain]);

            WriteArtifact(501, CreateCollision(FirstTerrain, withOverride: true));
            WriteArtifact(502, CreateCollision(SecondTerrain, withOverride: false));
        }

        public string WorkspacePath { get; }
        public string RootPath { get; }
        public MapBaseTerrain FirstTerrain { get; }
        public MapBaseTerrain SecondTerrain { get; }
        public MapDefinitionCatalog Definitions { get; }
        public MapBaseTerrainCatalog Terrains { get; }

        public string ArtifactPath(uint mapDataId) => Path.Combine(RootPath, $"{mapDataId}.ocmc");

        public FileMapTerrainCollisionCatalogRepository CreateRepository(MapCollisionLoadLimits? limits = null)
        {
            return new FileMapTerrainCollisionCatalogRepository(RootPath, limits ?? s_limits);
        }

        public void WriteArtifact(uint mapDataId, MapTerrainCollision collision)
        {
            FileMapTerrainCollisionCatalogRepositoryTests.WriteArtifact(ArtifactPath(mapDataId), collision);
        }

        public void Dispose()
        {
            Directory.Delete(WorkspacePath, recursive: true);
        }
    }
}
