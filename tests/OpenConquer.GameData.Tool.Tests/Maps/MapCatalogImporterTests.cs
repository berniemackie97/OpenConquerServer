using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenConquer.GameData.Tool.Maps;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapCatalogImporterTests
{
    private const string Source = """
        {
          "rows": [
            {
              "map_id": 1501,
              "map_data_id": 1730,
              "status": "resolved",
              "confidence": "B",
              "map_data_source": "TQ",
              "flags_basis": "TQ",
              "reason": "Corroborated source assignment",
              "flags_source_value": "13511951008464903",
              "flags_source_hex": "0x0030010C40150007",
              "flags_5517": "1152126353415",
              "flags_5517_hex": "0x0000010C40150007",
              "terrain": { "path": "map/map/fairylandPK-02.7z" }
            },
            {
              "map_id": 1961,
              "map_data_id": 1961,
              "status": "resolved",
              "confidence": "D",
              "map_data_source": "identity",
              "flags_basis": "DEFAULT_ZERO_NO_SOURCE",
              "reason": "Explicit working default",
              "flags_source_value": null,
              "flags_source_hex": null,
              "flags_5517": "0",
              "flags_5517_hex": "0x0000000000000000",
              "terrain": { "path": "map/map/mausoleum.7z" }
            },
            {
              "map_id": 1807,
              "map_data_id": null,
              "status": "unavailable",
              "confidence": "U",
              "map_data_source": "unresolved",
              "flags_basis": "n/a (unavailable)",
              "reason": "No verified terrain assignment",
              "flags_source_value": null,
              "flags_5517": null
            }
          ]
        }
        """;

    [Fact]
    public async Task Import_ValidSource_PreservesFlagsAndAvailability()
    {
        using TemporaryDirectory workspace = new();
        byte[] payload = Encoding.UTF8.GetBytes(Source);
        string destination = Path.Combine(workspace.Path, "catalog");

        (int resolved, int unavailable) = MapCatalogImporter.Import(payload, destination);

        Assert.Equal(2, resolved);
        Assert.Equal(1, unavailable);

        string definitionsPath = Path.Combine(destination, "map-definitions.json");
        string manifestPath = Path.Combine(destination, "catalog-manifest.json");

        Assert.True(File.Exists(definitionsPath));
        Assert.True(File.Exists(manifestPath));

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(definitionsPath, 1024 * 1024, 10_000));

        var catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);

        Assert.True(catalog.TryGet(1501, out var arena));
        Assert.Equal(1730u, arena.MapDataId);
        Assert.Equal(0x0030_010C_4015_0007UL, arena.Flags);

        Assert.True(catalog.TryGet(1961, out var mausoleum));
        Assert.Equal(0ul, mausoleum.Flags);

        Assert.False(catalog.TryGet(1807, out _));

        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllBytesAsync(manifestPath, TestContext.Current.CancellationToken));

        JsonElement root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)),
            root.GetProperty("sourceSha256").GetString());

        JsonElement maps = root.GetProperty("maps");
        Assert.Equal(3, maps.GetArrayLength());

        uint[] mapIds = maps.EnumerateArray()
            .Select(row => row.GetProperty("mapId").GetUInt32())
            .ToArray();

        Assert.Equal(new uint[] { 1501, 1807, 1961 }, mapIds);

        Dictionary<uint, JsonElement> records = maps.EnumerateArray()
            .ToDictionary(row => row.GetProperty("mapId").GetUInt32());

        JsonElement arenaRecord = records[1501];
        Assert.Equal("resolved", arenaRecord.GetProperty("status").GetString());
        Assert.Equal(1730u, arenaRecord.GetProperty("mapDataId").GetUInt32());
        Assert.Equal("13511951008464903", arenaRecord.GetProperty("flagsSourceValue").GetString());
        Assert.Equal("1152126353415", arenaRecord.GetProperty("clientFlags").GetString());

        JsonElement mausoleumRecord = records[1961];
        Assert.Equal("resolved", mausoleumRecord.GetProperty("status").GetString());
        Assert.Equal("DEFAULT_ZERO_NO_SOURCE", mausoleumRecord.GetProperty("flagsBasis").GetString());
        Assert.Equal(JsonValueKind.Null, mausoleumRecord.GetProperty("flagsSourceValue").ValueKind);
        Assert.Equal("0", mausoleumRecord.GetProperty("clientFlags").GetString());

        JsonElement unavailableRecord = records[1807];
        Assert.Equal("unavailable", unavailableRecord.GetProperty("status").GetString());
        Assert.Equal("U", unavailableRecord.GetProperty("confidence").GetString());
        Assert.Equal(JsonValueKind.Null, unavailableRecord.GetProperty("mapDataId").ValueKind);
        Assert.Equal(JsonValueKind.Null, unavailableRecord.GetProperty("flagsSourceValue").ValueKind);
        Assert.Equal(JsonValueKind.Null, unavailableRecord.GetProperty("clientFlags").ValueKind);
    }

    [Theory]
    [InlineData("\"flags_5517\": \"1152126353415\"", "\"flags_5517\": \"0\"")]
    [InlineData("\"flags_basis\": \"DEFAULT_ZERO_NO_SOURCE\"", "\"flags_basis\": \"TQ\"")]
    [InlineData("\"map_id\": 1807", "\"map_id\": 1501")]
    [InlineData("\"map_data_id\": null", "\"map_data_id\": 1730")]
    public void Import_InvalidSource_DoesNotPublishContent(string original, string replacement)
    {
        using TemporaryDirectory workspace = new();
        string destination = Path.Combine(workspace.Path, "catalog");
        string source = Source.Replace(original, replacement, StringComparison.Ordinal);

        Assert.NotEqual(Source, source);

        Assert.Throws<InvalidDataException>(() =>
            MapCatalogImporter.Import(Encoding.UTF8.GetBytes(source), destination));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void Import_ExistingDestination_IsNotReplaced()
    {
        using TemporaryDirectory workspace = new();
        string destination = Path.Combine(workspace.Path, "catalog");

        Directory.CreateDirectory(destination);

        string sentinel = Path.Combine(destination, "existing.txt");
        File.WriteAllText(sentinel, "preserve");

        Assert.Throws<IOException>(() =>
            MapCatalogImporter.Import(Encoding.UTF8.GetBytes(Source), destination));

        Assert.Equal("preserve", File.ReadAllText(sentinel));
    }

    [Fact]
    public void Import_SameSource_ProducesDeterministicContent()
    {
        using TemporaryDirectory workspace = new();
        byte[] payload = Encoding.UTF8.GetBytes(Source);

        string first = Path.Combine(workspace.Path, "first");
        string second = Path.Combine(workspace.Path, "second");

        MapCatalogImporter.Import(payload, first);
        MapCatalogImporter.Import(payload, second);

        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "map-definitions.json")),
            File.ReadAllBytes(Path.Combine(second, "map-definitions.json")));

        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "catalog-manifest.json")),
            File.ReadAllBytes(Path.Combine(second, "catalog-manifest.json")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-map-catalog-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path
        {
            get;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
