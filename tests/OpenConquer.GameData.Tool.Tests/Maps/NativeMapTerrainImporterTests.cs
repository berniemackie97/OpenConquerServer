using System.Text;
using OpenConquer.Application.World;
using OpenConquer.GameData.Tool.Maps;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class NativeMapTerrainImporterTests
{
    private static readonly MapTerrainLoadLimits s_limits =
        new(maximumWidth: 32, maximumHeight: 32, maximumCellsPerTerrain: 1024,
            maximumTotalCells: 4096, maximumExitsPerTerrain: 100,
            maximumArtifactBytes: 1024 * 1024, maximumContainerBytes: 1024 * 1024);

    [Fact]
    public async Task Generate_ValidNativeGrid_CreatesLoadableCanonicalTerrain()
    {
        using TemporaryDirectory workspace = new();
        string clientRoot = CreateNativeClient(workspace.Path, corruptedChecksum: false);
        string definitionsPath = CreateDefinitions(workspace.Path, [900001]);
        string outputDirectory = Path.Combine(workspace.Path, "output");

        int count = NativeMapTerrainImporter.Generate(clientRoot, definitionsPath, outputDirectory, s_limits,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "900001.ocbt")));

        MapDefinitionCatalog definitions = new([new OpenConquer.Domain.World.MapDefinition(1000, 900001, 0)]);
        FileMapBaseTerrainCatalogRepository repository = new(outputDirectory, s_limits);
        MapBaseTerrainCatalog catalog = await repository.LoadAsync(definitions, TestContext.Current.CancellationToken);

        Assert.True(catalog.TryGet(900001, out var terrain));
        Assert.Equal(2, terrain.Width);
        Assert.Equal(1, terrain.Height);
        Assert.Equal((short)-15, terrain.GetCell(0, 0).Elevation);
        Assert.Equal(7u, Assert.Single(terrain.Exits).PasswayIndex);
    }

    [Fact]
    public void Generate_InvalidNativeChecksum_DoesNotPublishDestination()
    {
        using TemporaryDirectory workspace = new();
        string clientRoot = CreateNativeClient(workspace.Path, corruptedChecksum: true);
        string definitionsPath = CreateDefinitions(workspace.Path, [900001]);
        string outputDirectory = Path.Combine(workspace.Path, "output");

        Assert.Throws<InvalidDataException>(() =>
            NativeMapTerrainImporter.Generate(clientRoot, definitionsPath, outputDirectory, s_limits,
                TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public void Generate_MissingSecondTerrain_DoesNotPublishPartialOutput()
    {
        using TemporaryDirectory workspace = new();
        string clientRoot = CreateNativeClient(workspace.Path, corruptedChecksum: false);
        string definitionsPath = CreateDefinitions(workspace.Path, [900001, 900002]);
        string outputDirectory = Path.Combine(workspace.Path, "output");

        Assert.Throws<InvalidDataException>(() =>
            NativeMapTerrainImporter.Generate(clientRoot, definitionsPath, outputDirectory, s_limits,
                TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public void Generate_ExistingDestination_IsNeverReplaced()
    {
        using TemporaryDirectory workspace = new();
        string clientRoot = CreateNativeClient(workspace.Path, corruptedChecksum: false);
        string definitionsPath = CreateDefinitions(workspace.Path, [900001]);
        string outputDirectory = Path.Combine(workspace.Path, "output");

        Directory.CreateDirectory(outputDirectory);
        string sentinel = Path.Combine(outputDirectory, "existing.txt");
        File.WriteAllText(sentinel, "preserve");

        Assert.Throws<IOException>(() =>
            NativeMapTerrainImporter.Generate(clientRoot, definitionsPath, outputDirectory, s_limits,
                TestContext.Current.CancellationToken));

        Assert.Equal("preserve", File.ReadAllText(sentinel));
    }

    [Fact]
    public void Generate_InvalidSevenZipSignature_IsRejected()
    {
        using TemporaryDirectory workspace = new();
        string clientRoot = CreateNativeClient(workspace.Path, corruptedChecksum: false);

        string mapDirectory = Path.Combine(clientRoot, "map");
        File.WriteAllBytes(Path.Combine(mapDirectory, "test.7z"), [1, 2, 3, 4, 5, 6]);

        WriteIndex(Path.Combine(clientRoot, "ini", "GameMap.dat"), "map/test.7z");
        string definitionsPath = CreateDefinitions(workspace.Path, [900001]);

        Assert.Throws<InvalidDataException>(() =>
            NativeMapTerrainImporter.Generate(clientRoot, definitionsPath, Path.Combine(workspace.Path, "output"),
                s_limits, TestContext.Current.CancellationToken));
    }

    private static string CreateNativeClient(string workspacePath, bool corruptedChecksum)
    {
        string root = Path.Combine(workspacePath, "client");
        Directory.CreateDirectory(Path.Combine(root, "ini"));
        Directory.CreateDirectory(Path.Combine(root, "map"));

        WriteIndex(Path.Combine(root, "ini", "GameMap.dat"), "map/test.DMap");

        byte[] payload = CreateDMap();

        if (corruptedChecksum)
        {
            payload[8 + 0x104 + 8] ^= 1;
        }

        File.WriteAllBytes(Path.Combine(root, "map", "test.DMap"), payload);

        return root;
    }

    private static void WriteIndex(string path, string relativePath)
    {
        byte[] name = Encoding.ASCII.GetBytes(relativePath);

        using FileStream stream = new(path, FileMode.Create);
        using BinaryWriter writer = new(stream);

        writer.Write(1u);
        writer.Write(900001u);
        writer.Write(checked((uint)name.Length));
        writer.Write(name);
        writer.Write(0u);
    }

    private static string CreateDefinitions(string workspacePath, uint[] ids)
    {
        string path = Path.Combine(workspacePath, "definitions.json");

        string maps = string.Join(",", ids.Select((id, index) =>
            $$"""{"mapId":{{1000 + index}},"mapDataId":{{id}},"flags":0}"""));

        File.WriteAllText(path, $$"""{"formatVersion":1,"maps":[{{maps}}]}""");

        return path;
    }

    private static byte[] CreateDMap()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0u);
        writer.Write(0u);
        writer.Write(new byte[0x104]);
        writer.Write(2u);
        writer.Write(1u);

        writer.Write((ushort)0);
        writer.Write((ushort)12);
        writer.Write((short)-15);

        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write((short)32);

        uint checksum = unchecked(
            (uint)((12 + 0 + 1) * 0)
            + (uint)((12 + 0 + 1) * (-15 + 2))
            + (uint)((24 + 0 + 1) * 1)
            + (uint)((24 + 1 + 1) * (32 + 2)));

        writer.Write(checksum);
        writer.Write(1u);
        writer.Write(100);
        writer.Write(-20);
        writer.Write(7u);
        writer.Flush();

        return stream.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-native-maps-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
