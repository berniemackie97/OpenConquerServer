using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Maps;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapBaseTerrainPipelineTests
{
    private static readonly MapTerrainLoadLimits s_limits =
        new(maximumWidth: 32, maximumHeight: 32, maximumCellsPerTerrain: 1024,
            maximumTotalCells: 4096, maximumExitsPerTerrain: 100,
            maximumArtifactBytes: 1024 * 1024, maximumContainerBytes: 1024 * 1024);

    private const string CustomSource =
        """
        {
          "formatVersion": 1,
          "mapDataId": 900001,
          "width": 2,
          "height": 1,
          "cells": [
            {"surfaceId": 12, "passabilityFlag": 0, "elevation": -15},
            {"surfaceId": 24, "passabilityFlag": 1, "elevation": 32}
          ],
          "exits": [{"x": 100, "y": -20, "passwayIndex": 7}]
        }
        """;

    [Fact]
    public void CanonicalWriter_SameTerrain_ProducesIdenticalBytes()
    {
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(CustomSource), s_limits);

        using TemporaryDirectory directory = new();
        string first = Path.Combine(directory.Path, "first.ocbt");
        string second = Path.Combine(directory.Path, "second.ocbt");

        MapBaseTerrainBinaryWriter.Write(first, terrain);
        MapBaseTerrainBinaryWriter.Write(second, terrain);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal((ushort)1, MapBaseTerrainBinaryWriter.FormatVersion);
    }

    [Fact]
    public async Task CanonicalWriterAndRuntimeLoader_RoundTripCustomTerrain()
    {
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(CustomSource), s_limits);

        using TemporaryDirectory directory = new();
        MapBaseTerrainBinaryWriter.Write(Path.Combine(directory.Path, "900001.ocbt"), terrain);

        MapDefinitionCatalog definitions = new([new MapDefinition(1000, 900001, 0)]);
        FileMapBaseTerrainCatalogRepository repository = new(directory.Path, s_limits);

        MapBaseTerrainCatalog catalog = await repository.LoadAsync(definitions, TestContext.Current.CancellationToken);

        Assert.True(catalog.TryGet(900001, out MapBaseTerrain? restored));
        Assert.Equal(2, restored.Width);
        Assert.Equal(1, restored.Height);
        Assert.Equal(new MapBaseTerrainCell(12, 0, -15), restored.GetCell(0, 0));
        Assert.Equal(new MapBaseTerrainCell(24, 1, 32), restored.GetCell(1, 0));
        Assert.Equal(new MapTerrainExit(100, -20, 7), Assert.Single(restored.Exits));
    }

    [Fact]
    public async Task RuntimeLoader_RejectsTamperedTerrain()
    {
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(CustomSource), s_limits);

        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "900001.ocbt");
        MapBaseTerrainBinaryWriter.Write(path, terrain);

        byte[] payload = File.ReadAllBytes(path);
        payload[24] ^= 0x01;
        File.WriteAllBytes(path, payload);

        FileMapBaseTerrainCatalogRepository repository = new(directory.Path, s_limits);
        MapDefinitionCatalog definitions = new([new MapDefinition(1000, 900001, 0)]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuntimeLoader_RejectsMismatchedMapDataId()
    {
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(CustomSource), s_limits);

        using TemporaryDirectory directory = new();
        MapBaseTerrainBinaryWriter.Write(Path.Combine(directory.Path, "900002.ocbt"), terrain);

        MapDefinitionCatalog definitions = new([new MapDefinition(1000, 900002, 0)]);
        FileMapBaseTerrainCatalogRepository repository = new(directory.Path, s_limits);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.LoadAsync(definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuntimeLoader_SharedMapDataId_LoadsOneTerrain()
    {
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(CustomSource), s_limits);

        using TemporaryDirectory directory = new();
        MapBaseTerrainBinaryWriter.Write(Path.Combine(directory.Path, "900001.ocbt"), terrain);

        MapDefinitionCatalog definitions = new([
            new MapDefinition(1000, 900001, 0),
            new MapDefinition(1001, 900001, 1),
        ]);

        FileMapBaseTerrainCatalogRepository repository = new(directory.Path, s_limits);
        MapBaseTerrainCatalog catalog = await repository.LoadAsync(definitions, TestContext.Current.CancellationToken);

        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public async Task RuntimeLoader_MissingRequiredTerrain_FailsClosed()
    {
        using TemporaryDirectory directory = new();

        MapDefinitionCatalog definitions = new([new MapDefinition(1000, 900001, 0)]);
        FileMapBaseTerrainCatalogRepository repository = new(directory.Path, s_limits);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await repository.LoadAsync(definitions, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"formatVersion":1,"mapDataId":0,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":0,"elevation":0}],"exits":[]}""")]
    [InlineData("""{"formatVersion":1,"mapDataId":1,"width":1,"height":1,"cells":[],"exits":[]}""")]
    [InlineData("""{"formatVersion":1,"mapDataId":1,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":65536,"elevation":0}],"exits":[]}""")]
    [InlineData("""{"formatVersion":1,"mapDataId":1,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":0,"elevation":32768}],"exits":[]}""")]
    [InlineData("""{"formatVersion":1,"mapDataId":1,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":0,"elevation":0,"unknown":1}],"exits":[]}""")]
    [InlineData("""{"formatVersion":1,"mapDataId":1,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":0,"passabilityFlag":1,"elevation":0}],"exits":[]}""")]
    [InlineData("""{"formatVersion":2,"mapDataId":1,"width":1,"height":1,"cells":[{"surfaceId":1,"passabilityFlag":0,"elevation":0}],"exits":[]}""")]
    public void AuthoringSource_InvalidContent_IsRejected(string content)
    {
        Assert.Throws<InvalidDataException>(() =>
            MapBaseTerrainSourceReader.Parse(Encoding.UTF8.GetBytes(content), s_limits));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-terrain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
