using System.Globalization;
using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Tests.Maps;

public sealed class MapCatalogBaselineTests
{
    [Fact]
    public async Task CanonicalCatalog_MatchesReviewedNative5517Baseline()
    {
        string contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Maps");
        string catalogPath = Path.Combine(contentDirectory, "map-definitions.json");
        string evidencePath = Path.Combine(
            contentDirectory,
            "map-definitions-5517-provenance.json"
        );

        FileMapDefinitionCatalogRepository repository = new(
            new MapDefinitionCatalogFileOptions(
                catalogPath,
                maximumFileLengthBytes: 1024 * 1024,
                maximumDefinitions: 10_000
            )
        );
        MapDefinitionCatalog catalog = await repository.LoadAsync(
            TestContext.Current.CancellationToken
        );
        using JsonDocument evidence = JsonDocument.Parse(
            await File.ReadAllBytesAsync(evidencePath, TestContext.Current.CancellationToken)
        );

        JsonElement evidenceMaps = evidence.RootElement.GetProperty("maps");
        Assert.Equal(289, evidenceMaps.GetArrayLength());
        Assert.Equal(282, catalog.Count);
        Assert.Equal(
            185,
            catalog.Definitions.Select(definition => definition.MapDataId).Distinct().Count()
        );

        int unavailableCount = 0;
        int defaultZeroCount = 0;
        int sourceHighBitsCount = 0;

        foreach (JsonElement row in evidenceMaps.EnumerateArray())
        {
            uint mapId = row.GetProperty("mapId").GetUInt32();
            string status = row.GetProperty("status").GetString()!;

            if (status == "unavailable")
            {
                unavailableCount++;
                Assert.False(catalog.TryGet(mapId, out _));
                continue;
            }

            Assert.Equal("resolved", status);
            Assert.True(catalog.TryGet(mapId, out MapDefinition? definition));
            Assert.Equal(row.GetProperty("mapDataId").GetUInt32(), definition.MapDataId);

            string basis = row.GetProperty("flagsBasis").GetString()!;
            string? sourceValue = row.GetProperty("flagsSourceValue").GetString();
            string wireValue = row.GetProperty("flags5517").GetString()!;
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
                expectedFlags = ulong.Parse(sourceValue, CultureInfo.InvariantCulture);
            }

            Assert.Equal(expectedFlags, definition.Flags);
            Assert.Equal(
                ulong.Parse(wireValue, CultureInfo.InvariantCulture),
                MapFlags.Project(definition.Flags)
            );

            if ((definition.Flags & ~MapFlags.ClientMask) != 0)
            {
                sourceHighBitsCount++;
            }
        }

        Assert.Equal(7, unavailableCount);
        Assert.Equal(48, defaultZeroCount);
        Assert.Equal(34, sourceHighBitsCount);

        Assert.True(catalog.TryGet(1501, out MapDefinition? classPkArena));
        Assert.Equal(1730u, classPkArena.MapDataId);
        Assert.Equal(0x0030_010C_4015_0007UL, classPkArena.Flags);
        Assert.Equal(0x0000_010C_4015_0007UL, MapFlags.Project(classPkArena.Flags));

        Assert.True(catalog.TryGet(1778, out MapDefinition? dangerCave));
        Assert.Equal(1013u, dangerCave.MapDataId);
        Assert.False(catalog.TryGet(2080, out _));
    }

    [Fact]
    public void Projection_PreservesClientBitsWithoutMutatingSourceFlags()
    {
        Assert.Equal(0UL, MapFlags.Project(0));
        Assert.Equal(1UL << 38, MapFlags.Project(1UL << 38));
        Assert.Equal(1UL << 40, MapFlags.Project(1UL << 40));
        Assert.Equal(0UL, MapFlags.Project(1UL << 41));
        Assert.Equal(MapFlags.ClientMask, MapFlags.Project(ulong.MaxValue));
    }
}
