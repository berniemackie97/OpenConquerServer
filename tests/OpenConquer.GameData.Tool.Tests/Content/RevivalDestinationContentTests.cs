using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Revival;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class RevivalDestinationContentTests
{
    [Fact]
    public void Resolve_UsesTargetTownPortalNotSourceCavePortal()
    {
        WorldMapContentCatalog maps = CreateMaps(1011, 1013);
        RevivalDestinationCatalog catalog = new(maps,
        [
            new MapRevivalSource(1011, 1011, 2, 3, "TQ2008"),
            new MapRevivalSource(1013, 1011, 1, 1, "TQ2008"),
        ]);

        Assert.Equal(new RevivalDestinationResolution(RevivalDestinationStatus.Resolved, 1011, 2, 3),
            catalog.Resolve(1013));
        Assert.Equal(new RevivalDestinationResolution(RevivalDestinationStatus.Resolved, 1011, 2, 3),
            catalog.Resolve(1011));
    }

    [Fact]
    public void Resolve_UnknownInvalidAndBlockedDestinationsNeverFallBack()
    {
        WorldMapContentCatalog maps = CreateMaps(1000, 1002, 1013, 1036, 2060);
        RevivalDestinationCatalog catalog = new(maps,
        [
            new MapRevivalSource(1000, 1002, 0, 0, "LongV6270"),
            new MapRevivalSource(1002, 1002, 1, 1, "LongV6270"),
            new MapRevivalSource(1013, 7777, 1, 1, "LongV6270"),
            new MapRevivalSource(1036, 1036, 1000, 1000, "LongV6270"),
            new MapRevivalSource(2060, 2060, 0, 0, "LongV6270"),
        ]);

        Assert.Equal(new RevivalDestinationResolution(RevivalDestinationStatus.Resolved, 1002, 1, 1),
            catalog.Resolve(1000));
        Assert.Equal(RevivalDestinationStatus.UnavailableTargetMap, catalog.Resolve(1013).Status);
        Assert.Equal(RevivalDestinationStatus.InvalidTargetPortal, catalog.Resolve(1036).Status);
        Assert.Equal(RevivalDestinationStatus.MissingTargetPortal, catalog.Resolve(2060).Status);
        Assert.Equal(RevivalDestinationStatus.UnavailableWorldMap, catalog.Resolve(9999).Status);
        Assert.False(catalog.Resolve(9999).IsResolved);
    }

    [Fact]
    public void Resolve_UnknownSourceAndTargetEvidenceRemainExplicit()
    {
        WorldMapContentCatalog maps = CreateMaps(1000, 1002);
        RevivalDestinationCatalog none = new(maps, []);
        Assert.Equal(RevivalDestinationStatus.MissingSourceEvidence, none.Resolve(1000).Status);

        RevivalDestinationCatalog noTarget = new(maps,
        [new MapRevivalSource(1000, 1002, 3, 3, "LongV6270")]);
        Assert.Equal(RevivalDestinationStatus.MissingTargetEvidence, noTarget.Resolve(1000).Status);
    }

    [Fact]
    public void Construct_DuplicateUnavailableOrUnprovenSourceFailsClosed()
    {
        WorldMapContentCatalog maps = CreateMaps(1002);
        MapRevivalSource valid = new(1002, 1002, 1, 1, "LongV6270");
        Assert.Throws<ArgumentException>(() => new RevivalDestinationCatalog(maps, [valid, valid]));
        Assert.Throws<ArgumentException>(() => new RevivalDestinationCatalog(maps,
            [new MapRevivalSource(9999, 1002, 1, 1, "LongV6270")]));
        Assert.Throws<ArgumentException>(() => new RevivalDestinationCatalog(maps,
            [new MapRevivalSource(1002, 1002, 1, 1, " ")]));
    }

    [Theory]
    [InlineData("""{"formatVersion":2,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":"LongV6270"}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":"LongV6270","unexpected":1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":"LongV6270"},{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":"LongV6270"}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":-1,"portal0Y":1,"provenance":"LongV6270"}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":""}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1}]}""")]
    [InlineData("""{"formatVersion":1,"maps":[{"mapId":1002,"rebornMapId":1002,"portal0X":1,"portal0Y":1,"provenance":"LongV6270","mapId":1002}]}""")]
    public void Parse_InvalidShapeIsRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => MapRevivalSourceReader.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_ValidHistoricalRecordIsNotReinterpreted()
    {
        const string json =
            """{"formatVersion":1,"maps":[{"mapId":1013,"rebornMapId":1011,"portal0X":22,"portal0Y":25,"provenance":"LongV6270"}]}""";
        MapRevivalSource source = Assert.Single(MapRevivalSourceReader.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(new MapRevivalSource(1013, 1011, 22, 25, "LongV6270"), source);
    }

    [Fact]
    public async Task ReleasedEvidence_ResolvesVerifiedTownExamplesAndRetainsUnknownMaps()
    {
        string contentDirectory = FindContentDirectory();
        WorldMapContentLoader loader = new(
            new FileMapDefinitionCatalogRepository(new MapDefinitionCatalogFileOptions(
                Path.Combine(contentDirectory, "map-definitions.json"), 1024 * 1024, 10_000)),
            new FileMapBaseTerrainCatalogRepository(
                Path.Combine(contentDirectory, "terrains"), MapTerrainLoadLimits.CreateDefault()),
            new FileMapTerrainCollisionCatalogRepository(
                Path.Combine(contentDirectory, "collision"), MapCollisionLoadLimits.CreateDefault()));

        WorldMapContentCatalog maps = await loader.LoadAsync(TestContext.Current.CancellationToken);
        MapRevivalSource[] evidence = await MapRevivalSourceReader.LoadFileAsync(
            Path.Combine(contentDirectory, "revival-destinations.json"), TestContext.Current.CancellationToken);
        RevivalDestinationCatalog catalog = new(maps, evidence);

        Assert.Equal(282, catalog.MapCount);
        Assert.Equal(207, catalog.SourceRecordCount);
        Assert.Equal(new RevivalDestinationResolution(RevivalDestinationStatus.Resolved, 1002, 430, 380),
            catalog.Resolve(1002));
        Assert.Equal(new RevivalDestinationResolution(RevivalDestinationStatus.Resolved, 1011, 193, 266),
            catalog.Resolve(1013));
        Assert.Equal(RevivalDestinationStatus.MissingTargetPortal, catalog.Resolve(2060).Status);
        Assert.Equal(RevivalDestinationStatus.InvalidTargetPortal, catalog.Resolve(1036).Status);
        Assert.Equal(RevivalDestinationStatus.MissingSourceEvidence, catalog.Resolve(1926).Status);
        Assert.Equal(RevivalDestinationStatus.UnavailableWorldMap, catalog.Resolve(2092).Status);
    }

    private static WorldMapContentCatalog CreateMaps(params uint[] mapIds)
    {
        List<WorldMapContent> result = [];
        foreach (uint mapId in mapIds)
        {
            MapBaseTerrain terrain = new(mapId, 8, 8,
                Enumerable.Repeat(new MapBaseTerrainCell(10, 0, -5), 64), []);
            result.Add(new WorldMapContent(new MapDefinition(mapId, mapId, 0),
                new MapTerrainCollision(terrain, [])));
        }
        return new WorldMapContentCatalog(result);
    }

    private static string FindContentDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "content", "maps");
            if (File.Exists(Path.Combine(candidate, "revival-destinations.json")) &&
                Directory.Exists(Path.Combine(candidate, "collision")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("Revival evidence content is missing from the repository.");
    }
}
