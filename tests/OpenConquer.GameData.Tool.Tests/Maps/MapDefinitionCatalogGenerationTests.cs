using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Maps;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapDefinitionCatalogGenerationTests
{
    private const string ValidSource =
        """
        {
          "formatVersion": 1,
          "maps": [
            {
              "mapId": 1003,
              "mapDataId": 1010,
              "flags": 18446744073709551615,
              "mapDataEvidence": "review/map-1003-terrain.md",
              "flagsEvidence": "review/map-1003-flags.md"
            },
            {
              "mapId": 1002,
              "mapDataId": 1010,
              "flags": 274877906960,
              "mapDataEvidence": "review/map-1002-terrain.md",
              "flagsEvidence": "review/map-1002-flags.md"
            }
          ]
        }
        """;

    private const string CustomContent =
        """
        {
          "formatVersion": 1,
          "maps": [
            {"mapId": 1003, "mapDataId": 1010, "flags": 18446744073709551615},
            {"mapId": 1002, "mapDataId": 1010, "flags": 274877906960}
          ]
        }
        """;

    [Fact]
    public void Parse_ValidSource_PreservesCompleteValuesAndSorts()
    {
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(ValidSource));

        Assert.Equal(2, definitions.Length);
        Assert.Equal(1002u, definitions[0].MapId);
        Assert.Equal(1010u, definitions[0].MapDataId);
        Assert.Equal(274877906960ul, definitions[0].Flags);
        Assert.Equal(1003u, definitions[1].MapId);
        Assert.Equal(ulong.MaxValue, definitions[1].Flags);
    }

    [Fact]
    public void Parse_CustomContent_DoesNotRequireNativeEvidence()
    {
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(CustomContent));

        Assert.Equal(2, definitions.Length);
        Assert.Equal(1002u, definitions[0].MapId);
        Assert.Equal(1003u, definitions[1].MapId);
        Assert.Equal(ulong.MaxValue, definitions[1].Flags);
    }

    [Fact]
    public void Parse_MixedHistoricalAndCustomRecords_IsAccepted()
    {
        const string source =
            """
            {
              "formatVersion": 1,
              "maps": [
                {"mapId": 1, "mapDataId": 2, "flags": 0, "mapDataEvidence": "import/terrain.md"},
                {"mapId": 3, "mapDataId": 2, "flags": 18446744073709551615, "flagsEvidence": "import/flags.md"},
                {"mapId": 4, "mapDataId": 5, "flags": 0}
              ]
            }
            """;

        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(source));

        Assert.Equal(3, definitions.Length);
        Assert.Equal(0ul, definitions[0].Flags);
        Assert.Equal(ulong.MaxValue, definitions[1].Flags);
        Assert.Equal(5u, definitions[2].MapDataId);
    }

    [Theory]
    [InlineData("""{"formatVersion":1,"maps":[]}""")]
    [InlineData("""{"formatVersion":2,"maps":[]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"mapDataEvidence":"","flagsEvidence":"source"}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"mapDataEvidence":"source","flagsEvidence":" "}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"mapDataEvidence":null}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"flagsEvidence":123}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":-1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":18446744073709551616}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":0,"mapDataId":2,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":0,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"unknown":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0,"flags":1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0},{"mapId":1,"mapDataId":3,"flags":0}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[null]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1,"mapDataId":2,"flags":0}],"unexpected":true}""")]
    public void Parse_InvalidSource_IsRejected(string source)
    {
        Assert.Throws<InvalidDataException>(() => MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(source)));
    }

    [Fact]
    public void Parse_OversizedSource_IsRejected()
    {
        byte[] payload = new byte[MapDefinitionSourceReader.MaximumSourceLengthBytes + 1];

        Assert.Throws<InvalidDataException>(() => MapDefinitionSourceReader.Parse(payload));
    }

    [Fact]
    public async Task Generate_CanonicalFile_RoundTripsThroughRuntimeLoader()
    {
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(ValidSource));

        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "map-definitions.json");

        MapDefinitionCatalogFileWriter.Write(path, definitions);

        Assert.Equal(FileMapDefinitionCatalogRepository.FormatVersion, MapDefinitionCatalogFileWriter.FormatVersion);

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(path, 1024 * 1024, 100));

        MapDefinitionCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);

        Assert.True(catalog.TryGet(1002, out MapDefinition? first));
        Assert.Equal(274877906960ul, first.Flags);

        Assert.True(catalog.TryGet(1003, out MapDefinition? second));
        Assert.Equal(ulong.MaxValue, second.Flags);
    }

    [Fact]
    public async Task Generate_CustomContent_RoundTripsWithoutNativeEvidence()
    {
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(CustomContent));

        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "map-definitions.json");

        MapDefinitionCatalogFileWriter.Write(path, definitions);

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(path, 1024 * 1024, 100));

        MapDefinitionCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet(1002, out MapDefinition? first));
        Assert.Equal(1010u, first.MapDataId);
        Assert.True(catalog.TryGet(1003, out MapDefinition? second));
        Assert.Equal(ulong.MaxValue, second.Flags);
    }

    [Fact]
    public void Write_HistoricalAndCustomInputs_ProduceIdenticalCanonicalBytes()
    {
        using TemporaryDirectory directory = new();
        string historicalPath = Path.Combine(directory.Path, "historical.json");
        string customPath = Path.Combine(directory.Path, "custom.json");

        MapDefinition[] historical = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(ValidSource));
        MapDefinition[] custom = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(CustomContent));

        MapDefinitionCatalogFileWriter.Write(historicalPath, historical);
        MapDefinitionCatalogFileWriter.Write(customPath, custom);

        Assert.Equal(File.ReadAllBytes(historicalPath), File.ReadAllBytes(customPath));
    }

    [Fact]
    public void Write_DifferentInputOrdering_ProducesIdenticalBytes()
    {
        using TemporaryDirectory directory = new();
        string firstPath = Path.Combine(directory.Path, "first.json");
        string secondPath = Path.Combine(directory.Path, "second.json");

        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(Encoding.UTF8.GetBytes(ValidSource));

        MapDefinitionCatalogFileWriter.Write(firstPath, definitions);
        MapDefinitionCatalogFileWriter.Write(secondPath, definitions.Reverse().ToArray());

        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
    }

    [Fact]
    public void Write_InvalidDefinitions_DoNotReplaceExistingContent()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "map-definitions.json");
        File.WriteAllText(path, "original");

        Assert.Throws<ArgumentException>(() => MapDefinitionCatalogFileWriter.Write(path, []));
        Assert.Throws<ArgumentException>(() => MapDefinitionCatalogFileWriter.Write(path, [new MapDefinition(1, 2, 0), null!]));
        Assert.Throws<ArgumentException>(() => MapDefinitionCatalogFileWriter.Write(path, [new MapDefinition(1, 2, 0), new MapDefinition(1, 3, 0)]));

        Assert.Equal("original", File.ReadAllText(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-maps-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
