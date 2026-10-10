using OpenConquer.Application.World;
using OpenConquer.GameData.Tool.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class NativeMapSourceFilesTests
{
    private static readonly MapTerrainLoadLimits s_limits = new(maximumWidth: 32, maximumHeight: 32, maximumCellsPerTerrain: 1024, maximumTotalCells: 4096, maximumExitsPerTerrain: 100, maximumArtifactBytes: 1024 * 1024, maximumContainerBytes: 1024 * 1024);

    [Fact]
    public void ResolveFile_UsesCaseInsensitiveAssetIdentity()
    {
        using TemporaryDirectory directory = new();

        string folder = Path.Combine(directory.Path, "Map", "Scene");
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "HOUSE.OBJ");
        File.WriteAllBytes(path, [1, 2, 3]);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Equal(path, source.ResolveFile("map/scene/house.obj"));
        Assert.Equal(path, source.ResolveFile(@"map\scene\house.obj"));
    }

    [Theory]
    [InlineData("../outside.obj")]
    [InlineData("map/../outside.obj")]
    [InlineData("map/./house.obj")]
    [InlineData("/map/house.obj")]
    [InlineData("C:/map/house.obj")]
    [InlineData("map//house.obj")]
    [InlineData("map/house.obj/")]
    public void ResolveFile_RejectsUnsafePaths(string relativePath)
    {
        using TemporaryDirectory directory = new();

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<InvalidDataException>(() => source.ResolveFile(relativePath));
    }

    [Fact]
    public void ResolveFile_MissingObject_IsNotTreatedAsEmptyContent()
    {
        using TemporaryDirectory directory = new();

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<FileNotFoundException>(() =>
            source.ResolveFile("map/scene/missing.obj"));
    }

    [Fact]
    public void ResolveFile_AmbiguousCaseInsensitiveNames_AreRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TemporaryDirectory directory = new();

        File.WriteAllBytes(Path.Combine(directory.Path, "House.obj"), [1]);
        File.WriteAllBytes(Path.Combine(directory.Path, "HOUSE.OBJ"), [2]);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<InvalidDataException>(() =>
            source.ResolveFile("house.obj"));
    }

    [Fact]
    public void ResolveFile_SymbolicLink_IsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = new();
        using TemporaryDirectory external = new();

        string target = Path.Combine(external.Path, "house.obj");
        File.WriteAllBytes(target, [1, 2, 3]);

        File.CreateSymbolicLink(Path.Combine(directory.Path, "house.obj"), target);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<IOException>(() =>
            source.ResolveFile("house.obj"));
    }

    [Fact]
    public void ResolveFile_SymbolicLinkDirectory_IsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory directory = new();
        using TemporaryDirectory external = new();

        File.WriteAllBytes(Path.Combine(external.Path, "house.obj"), [1]);

        Directory.CreateSymbolicLink(
            Path.Combine(directory.Path, "scene"), external.Path);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<IOException>(() =>
            source.ResolveFile("scene/house.obj"));
    }

    [Fact]
    public void ReadMap_UnsupportedContainerExtension_IsRejected()
    {
        using TemporaryDirectory directory = new();

        File.WriteAllBytes(Path.Combine(directory.Path, "test.zip"), [1, 2, 3]);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<InvalidDataException>(() => source.ReadMap("test.zip", stream => stream.ReadByte(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReadMap_InvalidSevenZipSignature_IsRejected()
    {
        using TemporaryDirectory directory = new();

        File.WriteAllBytes(Path.Combine(directory.Path, "test.7z"), [1, 2, 3, 4, 5, 6]);

        NativeMapSourceFiles source = new(directory.Path, s_limits);

        Assert.Throws<InvalidDataException>(() => source.ReadMap("test.7z", stream => stream.ReadByte(), TestContext.Current.CancellationToken));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"openconquer-client-map-source-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
