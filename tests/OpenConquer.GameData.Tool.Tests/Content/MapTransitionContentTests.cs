using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Content;

public sealed class MapTransitionContentTests
{
    [Fact]
    public async Task CanonicalTransitionCatalog_PinsProvenanceAndUnresolvedExits()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Content", "Maps", "map-transitions.json");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("4b5258a9d70fd04e87e76fc9be00c4bb8464638f1f2f6d449e225d8cd9d62ea0",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        JsonElement transitions = root.GetProperty("transitions");
        Assert.Equal(373, transitions.GetArrayLength());

        int resolved = 0;
        int blocked = 0;
        int missing = 0;
        int shared = 0;
        HashSet<(uint MapId, uint PasswayIndex)> unique = [];

        foreach (JsonElement transition in transitions.EnumerateArray())
        {
            uint mapId = transition.GetProperty("mapId").GetUInt32();
            uint passwayIndex = transition.GetProperty("passwayIndex").GetUInt32();
            Assert.True(unique.Add((mapId, passwayIndex)));

            switch (transition.GetProperty("status").GetString())
            {
                case "resolved":
                    resolved++;
                    Assert.True(transition.TryGetProperty("targetMapId", out _));
                    Assert.Contains(transition.GetProperty("source").GetString(), new[] { "L", "T7", "Q" });
                    break;
                case "blocked-destination": blocked++; break;
                case "no-source": missing++; break;
                case "shared-terrain-unresolved": shared++; break;
                default: Assert.Fail($"Unknown transition status for ({mapId},{passwayIndex})."); break;
            }
        }

        Assert.Equal(322, resolved);
        Assert.Equal(3, blocked);
        Assert.Equal(41, missing);
        Assert.Equal(7, shared);
    }

    [Fact]
    public async Task CanonicalTransitionCatalog_ResolvesAgainstReleasedTerrainAndComposedCollision()
    {
        string directory = FindContentDirectory();
        WorldMapContentLoader loader = new(
            new FileMapDefinitionCatalogRepository(new MapDefinitionCatalogFileOptions(
                Path.Combine(directory, "map-definitions.json"), 1024 * 1024, 10_000)),
            new FileMapBaseTerrainCatalogRepository(Path.Combine(directory, "terrains"), MapTerrainLoadLimits.CreateDefault()),
            new FileMapTerrainCollisionCatalogRepository(Path.Combine(directory, "collision"), MapCollisionLoadLimits.CreateDefault()));

        WorldMapContentCatalog maps = await loader.LoadAsync(TestContext.Current.CancellationToken);
        FileMapTransitionCatalogRepository repository = new(
            Path.Combine(directory, "map-transitions.json"), 1024 * 1024, 1_000);
        MapTransitionCatalog catalog = await repository.LoadAsync(maps, TestContext.Current.CancellationToken);

        Assert.Equal(373, catalog.Count);
        Assert.Equal(322, catalog.ResolvedCount);
        Assert.Equal(51, catalog.UnresolvedCount);
        Assert.True(catalog.TryResolve(1000, 0, out MapTransitionDestination? transition));
        Assert.Equal((1001u, 312, 646), (transition.MapId, transition.X, transition.Y));
        Assert.False(catalog.TryResolve(1015, 10, out _));
    }

    private static string FindContentDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "content", "maps");
            if (File.Exists(Path.Combine(candidate, "map-definitions.json"))
                && Directory.Exists(Path.Combine(candidate, "terrains"))
                && Directory.Exists(Path.Combine(candidate, "collision")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Canonical map content directory was not found.");
    }

}
