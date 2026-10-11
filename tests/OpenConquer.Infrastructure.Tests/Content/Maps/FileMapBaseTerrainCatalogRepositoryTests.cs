using System.Security.Cryptography;
using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.Infrastructure.Tests.Content.Maps;

public sealed class FileMapBaseTerrainCatalogRepositoryTests
{
    [Fact]
    public async Task LoadAsync_CompleteDirectory_LoadsRequiredTerrain()
    {
        using Fixture fixture = new();

        MapBaseTerrainCatalog catalog = await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken);

        Assert.Equal(1, catalog.Count);
        Assert.True(catalog.TryGet(501, out MapBaseTerrain? terrain));
        Assert.Equal(501u, terrain.MapDataId);
        Assert.Equal(new MapBaseTerrainCell(10, 0, -15), terrain.GetCell(0, 0));
    }

    [Fact]
    public async Task LoadAsync_SharedMapDataId_RequiresOneArtifact()
    {
        using Fixture fixture = new();

        MapBaseTerrainCatalog catalog = await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Definitions.Count);
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public async Task LoadAsync_MissingDirectory_IsRejected()
    {
        using Fixture fixture = new();
        FileMapBaseTerrainCatalogRepository repository = new(Path.Combine(fixture.WorkspacePath, "missing"), MapTerrainLoadLimits.CreateDefault());

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            await repository.LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_MissingArtifact_IsRejected()
    {
        using Fixture fixture = new();
        File.Delete(fixture.ArtifactPath);

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));

        Assert.Contains("501.ocbt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_UnexpectedArtifact_IsRejected()
    {
        using Fixture fixture = new();
        File.WriteAllBytes(Path.Combine(fixture.RootPath, "502.ocbt"), []);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_UnexpectedNonArtifactFile_IsRejected()
    {
        using Fixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.RootPath, "unexpected.txt"), "unexpected");

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_UnexpectedSubdirectory_IsRejected()
    {
        using Fixture fixture = new();
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "nested"));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_NonCanonicalFilenameCase_IsRejected()
    {
        using Fixture fixture = new();

        byte[] payload = File.ReadAllBytes(fixture.ArtifactPath);
        File.Delete(fixture.ArtifactPath);
        File.WriteAllBytes(Path.Combine(fixture.RootPath, "501.OCBT"), payload);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_DoesNotReadContent()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, cancellation.Token));
    }

    [Fact]
    public async Task LoadAsync_SymbolicLinkDirectory_IsRejected()
    {
        using Fixture fixture = new();

        string linkPath = Path.Combine(fixture.WorkspacePath, "terrain-link");
        Directory.CreateSymbolicLink(linkPath, fixture.RootPath);

        FileMapBaseTerrainCatalogRepository repository = new(linkPath, MapTerrainLoadLimits.CreateDefault());

        await Assert.ThrowsAsync<IOException>(async () =>
            await repository.LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_SymbolicLinkArtifact_IsRejected()
    {
        using Fixture fixture = new();

        string actualPath = Path.Combine(fixture.WorkspacePath, "source.ocbt");
        File.Move(fixture.ArtifactPath, actualPath);
        File.CreateSymbolicLink(fixture.ArtifactPath, actualPath);

        await Assert.ThrowsAsync<IOException>(async () =>
            await fixture.CreateRepository().LoadAsync(fixture.Definitions, TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            WorkspacePath = Path.Combine(Path.GetTempPath(), $"openconquer-terrain-catalog-{Guid.NewGuid():N}");
            RootPath = Path.Combine(WorkspacePath, "terrains");
            Directory.CreateDirectory(RootPath);

            Definitions = new MapDefinitionCatalog([
                new MapDefinition(1000, 501, 0),
                new MapDefinition(1001, 501, 1)
            ]);

            WriteArtifact(ArtifactPath);
        }

        public string WorkspacePath { get; }
        public string RootPath { get; }
        public string ArtifactPath => Path.Combine(RootPath, "501.ocbt");
        public MapDefinitionCatalog Definitions { get; }

        public FileMapBaseTerrainCatalogRepository CreateRepository()
        {
            return new FileMapBaseTerrainCatalogRepository(RootPath, MapTerrainLoadLimits.CreateDefault());
        }

        private static void WriteArtifact(string path)
        {
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write("OCBT"u8);
            writer.Write((ushort)1);
            writer.Write((ushort)0);
            writer.Write(501u);
            writer.Write(1u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write((ushort)10);
            writer.Write((ushort)0);
            writer.Write((short)-15);

            writer.Flush();
            stream.Position = 0;

            byte[] checksum = SHA256.HashData(stream);
            stream.Position = stream.Length;
            writer.Write(checksum);
            writer.Flush();
        }

        public void Dispose() => Directory.Delete(WorkspacePath, recursive: true);
    }
}
