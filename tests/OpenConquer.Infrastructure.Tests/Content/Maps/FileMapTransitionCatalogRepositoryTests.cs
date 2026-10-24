using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.Infrastructure.Tests.Content.Maps;

public sealed class FileMapTransitionCatalogRepositoryTests
{
    [Fact]
    public async Task LoadAsync_ResolvedAndUnresolvedNativeExits_ArePreserved()
    {
        using TemporaryFile file = new(
            """
            {"formatVersion":1,"transitions":[
              {"mapId":100,"passwayIndex":0,"status":"resolved","source":"L","targetMapId":100,"targetPortalIndex":3,"targetX":1,"targetY":1},
              {"mapId":100,"passwayIndex":2,"status":"no-source"}
            ]}
            """);

        MapTransitionCatalog transitions = await CreateRepository(file.Path).LoadAsync(CreateWorld(), TestContext.Current.CancellationToken);

        Assert.Equal(2, transitions.Count);
        Assert.Equal(1, transitions.ResolvedCount);
        Assert.Equal(1, transitions.UnresolvedCount);
        Assert.True(transitions.TryResolve(100, 0, out MapTransitionDestination? destination));
        Assert.Equal((100u, 3u, 1, 1, "L"), (destination.MapId, destination.PortalIndex, destination.X, destination.Y, destination.EvidenceSource));
        Assert.False(transitions.TryResolve(100, 2, out _));
        Assert.False(transitions.TryResolve(200, 0, out _));
    }

    [Fact]
    public async Task LoadAsync_HistoricalCandidateIsNotReleasedWithoutApproval()
    {
        using TemporaryFile approved = new(
            """
            {"formatVersion":1,"transitions":[
              {"mapId":100,"passwayIndex":0,"status":"release-approved","source":"L","targetMapId":100,"targetPortalIndex":3,"targetX":1,"targetY":1},
              {"mapId":100,"passwayIndex":2,"status":"no-source"}
            ]}
            """);
        MapTransitionCatalog released = await CreateRepository(approved.Path).LoadAsync(
            CreateWorld(), TestContext.Current.CancellationToken);
        Assert.Equal(1, released.ReleasedCount);
        Assert.True(released.TryResolveReleased(100, 0, out _));

        using TemporaryFile candidate = new(
            """
            {"formatVersion":1,"transitions":[
              {"mapId":100,"passwayIndex":0,"status":"resolved","source":"L","targetMapId":100,"targetPortalIndex":3,"targetX":1,"targetY":1},
              {"mapId":100,"passwayIndex":2,"status":"no-source"}
            ]}
            """);
        MapTransitionCatalog historical = await CreateRepository(candidate.Path).LoadAsync(
            CreateWorld(), TestContext.Current.CancellationToken);
        Assert.Equal(0, historical.ReleasedCount);
        Assert.True(historical.TryResolve(100, 0, out _));
        Assert.False(historical.TryResolveReleased(100, 0, out _));
    }

    [Theory]
    [InlineData("""{"formatVersion":2,"transitions":[]}""")]
    [InlineData("""{"formatVersion":1,"transitions":null}""")]
    [InlineData("""{"formatVersion":1,"transitions":[null]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"no-source"},{"mapId":100,"passwayIndex":0,"status":"no-source"},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":9,"status":"no-source"},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"unknown"},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"resolved","source":"Z","targetMapId":100,"targetPortalIndex":1,"targetX":1,"targetY":1},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"resolved","source":"L","targetMapId":999,"targetPortalIndex":1,"targetX":1,"targetY":1},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"resolved","source":"L","targetMapId":100,"targetPortalIndex":1,"targetX":20,"targetY":1},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"no-source","targetMapId":100},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"no-source","status":"no-source"},{"mapId":100,"passwayIndex":2,"status":"no-source"}]}""")]
    [InlineData("""{"formatVersion":1,"transitions":[{"mapId":100,"passwayIndex":0,"status":"no-source"},{"mapId":100,"passwayIndex":2,"status":"no-source"}],"extra":1}""")]
    public async Task LoadAsync_InvalidCatalog_FailsClosed(string json)
    {
        using TemporaryFile file = new(json);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CreateRepository(file.Path).LoadAsync(CreateWorld(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_BlockedDestination_IsRejected()
    {
        using TemporaryFile file = new(
            """
            {"formatVersion":1,"transitions":[
              {"mapId":100,"passwayIndex":0,"status":"resolved","source":"L","targetMapId":100,"targetPortalIndex":1,"targetX":0,"targetY":1},
              {"mapId":100,"passwayIndex":2,"status":"no-source"}
            ]}
            """);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await CreateRepository(file.Path).LoadAsync(CreateWorld(blocked: true), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_ExcessiveSizeOrRows_FailsClosed()
    {
        using TemporaryFile file = new("""{"formatVersion":1,"transitions":[]}""");
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new FileMapTransitionCatalogRepository(file.Path, 1, 100).LoadAsync(CreateWorld(), TestContext.Current.CancellationToken));
        using TemporaryFile extra = new(
            """
            {"formatVersion":1,"transitions":[
              {"mapId":100,"passwayIndex":0,"status":"no-source"},
              {"mapId":100,"passwayIndex":2,"status":"no-source"}
            ]}
            """);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new FileMapTransitionCatalogRepository(extra.Path, 1024, 1).LoadAsync(CreateWorld(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_PreCanceled_DoesNotRead()
    {
        using TemporaryFile file = new("not JSON");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CreateRepository(file.Path).LoadAsync(CreateWorld(), cancellation.Token));
    }

    private static WorldMapContentCatalog CreateWorld(bool blocked = false)
    {
        MapBaseTerrain terrain = new(100, 2, 2, Enumerable.Repeat(new MapBaseTerrainCell(0, 0, 0), 4),
            [new MapTerrainExit(0, 0, 0), new MapTerrainExit(1, 0, 2)]);
        MapTerrainCollisionOverride[] overrides = blocked
            ? [new MapTerrainCollisionOverride(2, new MapBaseTerrainCell(0, 1, 0))]
            : [];
        MapTerrainCollision collision = new(terrain, overrides);
        return new WorldMapContentCatalog([new WorldMapContent(new MapDefinition(100, 100, 0), collision)]);
    }

    private static FileMapTransitionCatalogRepository CreateRepository(string path) => new(path, 1024 * 1024, 100);
}
