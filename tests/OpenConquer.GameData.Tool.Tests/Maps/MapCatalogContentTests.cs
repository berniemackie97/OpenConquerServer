using System.Globalization;
using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapCatalogContentTests
{
    private const ulong SupportedClientFlagMask = (1UL << 41) - 1;

    [Fact]
    public async Task CanonicalCatalog_MatchesSourceManifest()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Content", "Maps");
        string catalogPath = Path.Combine(directory, "map-definitions.json");
        string manifestPath = Path.Combine(directory, "catalog-manifest.json");

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(catalogPath, maximumFileLengthBytes: 1024 * 1024,
                maximumDefinitions: 10_000));

        MapDefinitionCatalog catalog = await repository.LoadAsync(TestContext.Current.CancellationToken);
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllBytesAsync(manifestPath, TestContext.Current.CancellationToken));

        JsonElement root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(64, root.GetProperty("sourceSha256").GetString()!.Length);

        JsonElement maps = root.GetProperty("maps");

        Assert.Equal(289, maps.GetArrayLength());
        Assert.Equal(282, catalog.Count);
        Assert.Equal(185, catalog.Definitions.Select(definition => definition.MapDataId).Distinct().Count());

        HashSet<uint> observed = [];
        int resolvedCount = 0;
        int unavailableCount = 0;
        int defaultZeroCount = 0;
        int sourceHighBitsCount = 0;

        foreach (JsonElement row in maps.EnumerateArray())
        {
            uint mapId = row.GetProperty("mapId").GetUInt32();
            Assert.True(observed.Add(mapId), $"Duplicate map ID {mapId} in content manifest.");

            string status = row.GetProperty("status").GetString()!;

            if (status == "unavailable")
            {
                unavailableCount++;
                Assert.False(catalog.TryGet(mapId, out _));
                Assert.Equal(JsonValueKind.Null, row.GetProperty("mapDataId").ValueKind);
                Assert.Equal(JsonValueKind.Null, row.GetProperty("flagsSourceValue").ValueKind);
                Assert.Equal(JsonValueKind.Null, row.GetProperty("clientFlags").ValueKind);
                continue;
            }

            Assert.Equal("resolved", status);
            resolvedCount++;

            Assert.True(catalog.TryGet(mapId, out MapDefinition? definition));
            Assert.Equal(row.GetProperty("mapDataId").GetUInt32(), definition.MapDataId);

            string basis = row.GetProperty("flagsBasis").GetString()!;
            string? sourceValue = row.GetProperty("flagsSourceValue").GetString();

            ulong expectedFlags;

            if (basis == "DEFAULT_ZERO_NO_SOURCE")
            {
                defaultZeroCount++;
                Assert.Null(sourceValue);
                expectedFlags = 0;
            }
            else
            {
                Assert.NotNull(sourceValue);
                Assert.True(ulong.TryParse(sourceValue, NumberStyles.None, CultureInfo.InvariantCulture,
                    out expectedFlags), $"Map {mapId} has invalid source flags.");
            }

            Assert.Equal(expectedFlags, definition.Flags);

            Assert.True(row.TryGetProperty("clientFlags", out JsonElement clientFlags),
                $"Map {mapId} is missing clientFlags.");

            Assert.Equal(JsonValueKind.String, clientFlags.ValueKind);

            Assert.True(ulong.TryParse(clientFlags.GetString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out ulong expectedClientFlags),
                $"Map {mapId} has invalid client flags.");

            Assert.Equal(definition.Flags & SupportedClientFlagMask, expectedClientFlags);

            if ((definition.Flags & ~SupportedClientFlagMask) != 0)
            {
                sourceHighBitsCount++;
            }
        }

        Assert.Equal(maps.GetArrayLength(), observed.Count);
        Assert.Equal(catalog.Count, resolvedCount);
        Assert.Equal(7, unavailableCount);
        Assert.Equal(48, defaultZeroCount);
        Assert.Equal(34, sourceHighBitsCount);

        Assert.True(catalog.TryGet(1501, out MapDefinition? classPkArena));
        Assert.Equal(1730u, classPkArena.MapDataId);
        Assert.Equal(0x0030_010C_4015_0007UL, classPkArena.Flags);

        Assert.True(catalog.TryGet(1778, out MapDefinition? dangerCave));
        Assert.Equal(1013u, dangerCave.MapDataId);

        Assert.False(catalog.TryGet(2080, out _));

        foreach (uint unavailableMapId in new uint[] { 1807, 1808, 1809, 1810, 1889, 1951, 2092 })
        {
            Assert.False(catalog.TryGet(unavailableMapId, out _));
        }
    }
}
