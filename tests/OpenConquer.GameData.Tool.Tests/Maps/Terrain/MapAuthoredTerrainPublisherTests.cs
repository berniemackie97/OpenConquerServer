using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Maps.Terrain;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps.Terrain;

public sealed class MapAuthoredTerrainPublisherTests
{
    private const string Source =
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
    public async Task BuildPublishesPairAcceptedByProductionCatalogReaders()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "input.json");
        string destination = Path.Combine(directory.Path, "result");
        File.WriteAllText(source, Source);

        uint mapDataId = await MapAuthoredTerrainPublisher.PublishAsync(source, destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(900001u, mapDataId);
        Assert.Equal(["collisions", "terrains"],
            Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Order().ToArray());
        string terrainRoot = Path.Combine(destination, "terrains");
        string collisionRoot = Path.Combine(destination, "collisions");
        Assert.Equal(["900001.ocbt"], Directory.EnumerateFiles(terrainRoot).Select(Path.GetFileName).ToArray());
        Assert.Equal(["900001.ocmc"], Directory.EnumerateFiles(collisionRoot).Select(Path.GetFileName).ToArray());

        MapDefinitionCatalog definitions = new([new MapDefinition(900001, 900001, 0)]);
        MapBaseTerrainCatalog terrains = await new FileMapBaseTerrainCatalogRepository(terrainRoot, MapTerrainLoadLimits.CreateDefault())
            .LoadAsync(definitions, TestContext.Current.CancellationToken);
        Assert.True(terrains.TryGet(mapDataId, out MapBaseTerrain? terrain));
        Assert.Equal(new MapBaseTerrainCell(12, 0, -15), terrain.GetCell(0, 0));
        Assert.Equal(new MapBaseTerrainCell(24, 1, 32), terrain.GetCell(1, 0));
        Assert.Equal(new MapTerrainExit(100, -20, 7), Assert.Single(terrain.Exits));

        MapTerrainCollisionCatalog collisions = await new FileMapTerrainCollisionCatalogRepository(
            collisionRoot, MapCollisionLoadLimits.CreateDefault())
            .LoadAsync(definitions, terrains, TestContext.Current.CancellationToken);
        Assert.True(collisions.TryGet(mapDataId, out MapTerrainCollision? collision));
        Assert.Empty(collision.Overrides);
        Assert.Equal(terrain.GetCell(1, 0), collision.GetEffectiveCell(1, 0));
        Assert.Equal(MapBaseTerrainFingerprint.Compute(terrain, TestContext.Current.CancellationToken),
            File.ReadAllBytes(Path.Combine(collisionRoot, "900001.ocmc")).AsSpan(24, 32).ToArray());
    }

    [Fact]
    public async Task IdenticalSourcesProduceIdenticalTerrainAndCollisionBytes()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "input.json");
        File.WriteAllText(source, Source);
        string first = Path.Combine(directory.Path, "first");
        string second = Path.Combine(directory.Path, "second");

        await MapAuthoredTerrainPublisher.PublishAsync(source, first, TestContext.Current.CancellationToken);
        await MapAuthoredTerrainPublisher.PublishAsync(source, second, TestContext.Current.CancellationToken);

        foreach (string relative in new[] { "terrains/900001.ocbt", "collisions/900001.ocmc" })
        {
            string[] segments = relative.Split('/');
            byte[] a = File.ReadAllBytes(Path.Combine(first, Path.Combine(segments)));
            byte[] b = File.ReadAllBytes(Path.Combine(second, Path.Combine(segments)));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public async Task InvalidSourceOrCanceledBuildDoesNotPublishAnyArtifacts()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "input.json");
        string destination = Path.Combine(directory.Path, "result");
        File.WriteAllText(source, Source.Replace("\"width\": 2", "\"width\": 3", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await MapAuthoredTerrainPublisher.PublishAsync(source, destination, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(destination));

        File.WriteAllText(source, Source);
        using (CancellationTokenSource cancellation = new())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await MapAuthoredTerrainPublisher.PublishAsync(source, destination, cancellation.Token));
        }

        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".result.*.tmp"));
    }

    [Fact]
    public async Task ExistingOutputCannotBeReplaced()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "input.json");
        File.WriteAllText(source, Source);
        string destination = Path.Combine(directory.Path, "result");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "existing.txt");
        File.WriteAllText(sentinel, "keep");

        await Assert.ThrowsAsync<IOException>(async () =>
            await MapAuthoredTerrainPublisher.PublishAsync(source, destination, TestContext.Current.CancellationToken));

        Assert.Equal("keep", File.ReadAllText(sentinel));
        Assert.Equal(["existing.txt"], Directory.EnumerateFiles(destination).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".result.*.tmp"));
    }
}
