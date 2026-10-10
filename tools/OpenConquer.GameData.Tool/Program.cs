using System.Security.Cryptography;
using OpenConquer.Application.World;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Items;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.IO;
using OpenConquer.GameData.Tool.Items;
using OpenConquer.GameData.Tool.Maps;

namespace OpenConquer.GameData.Tool;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int OperationFailedExitCode = 1;
    private const int InvalidArgumentsExitCode = 2;
    private const int MaximumItemTypeSourceLengthBytes = 64 * 1024 * 1024;

    private static async Task<int> Main(string[] args)
    {
        bool validArguments = args is [("generate-item-types" or "generate-map-definitions" or "generate-base-terrain" or "import-map-catalog"), _, _]
            or ["generate-map-terrains", _, _, _]
            or ["verify-map-collision-sources", _, _, _];

        if (!validArguments)
        {
            PrintUsage();
            return InvalidArgumentsExitCode;
        }

        try
        {
            string sourcePath = Path.GetFullPath(args[1]);
            string destinationPath = Path.GetFullPath(args[2]);

            if (args[0] == "generate-map-terrains")
            {
                int count = NativeMapTerrainImporter.Generate(sourcePath, destinationPath,
                    Path.GetFullPath(args[3]), MapTerrainLoadLimits.CreateDefault());

                Console.WriteLine($"Generated {count} canonical base-terrain artifacts.");
                return SuccessExitCode;
            }

            if (args[0] == "verify-map-collision-sources")
            {
                return await VerifyMapCollisionSourcesAsync(sourcePath, destinationPath, Path.GetFullPath(args[3])).ConfigureAwait(false);
            }

            StringComparison pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(sourcePath, destinationPath, pathComparison))
            {
                Console.Error.WriteLine("OpenConquer.GameData.Tool: source and destination paths must differ.");
                return InvalidArgumentsExitCode;
            }

            return args[0] switch
            {
                "generate-item-types" => GenerateItemTypes(sourcePath, destinationPath),
                "generate-map-definitions" => GenerateMapDefinitions(sourcePath, destinationPath),
                "generate-base-terrain" => GenerateBaseTerrain(sourcePath, destinationPath),
                "import-map-catalog" => ImportMapCatalog(sourcePath, destinationPath),
                _ => InvalidArgumentsExitCode
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException
            or IOException or UnauthorizedAccessException or NotSupportedException or OverflowException)
        {
            Console.Error.WriteLine($"OpenConquer.GameData.Tool: {exception.Message}");
            return OperationFailedExitCode;
        }
    }

    private static async Task<int> VerifyMapCollisionSourcesAsync(
        string assetRootPath, string definitionsPath, string terrainDirectory)
    {
        MapCollisionSourceReport report = await MapCollisionSourceVerifier.VerifyAsync(
            assetRootPath, definitionsPath, terrainDirectory,
            MapTerrainLoadLimits.CreateDefault(), MapCollisionSourceLimits.CreateDefault())
            .ConfigureAwait(false);

        foreach (MapCollisionSourceResult terrain in report.Terrains)
        {
            Console.WriteLine($"MapDataId {terrain.MapDataId}: scenery={terrain.ScenerySubcomponents}, " +
                              $"groups={terrain.TerrainObjectGroups}, lists={terrain.ObjectListFiles}, " +
                              $"attachments={terrain.Attachments}, overrides={terrain.EffectiveOverrides}");
        }

        Console.WriteLine($"Verified {report.TerrainCount} terrain identities; " +
                          $"groups={report.TotalGroups}, attachments={report.TotalAttachments}, " +
                          $"effective overrides={report.TotalEffectiveOverrides}, " +
                          $"object-list bytes={report.TotalObjectBytes}.");

        return SuccessExitCode;
    }

    private static int ImportMapCatalog(string sourcePath, string destinationDirectory)
    {
        byte[] payload = ToolSourceFileReader.Read(sourcePath, MapCatalogSourceReader.MaximumSourceLengthBytes);
        (int resolved, int unavailable) = MapCatalogImporter.Import(payload, destinationDirectory);

        Console.WriteLine($"Imported {resolved} resolved maps and {unavailable} unavailable map records.");
        Console.WriteLine($"Source SHA-256: {ComputeSha256(payload)}");

        return SuccessExitCode;
    }

    private static int GenerateItemTypes(string sourcePath, string destinationPath)
    {
        byte[] sourcePayload = ToolSourceFileReader.Read(sourcePath, MaximumItemTypeSourceLengthBytes);
        ItemTypeDatTable source = ItemTypeDatTable.Parse(sourcePayload);
        ItemTypeDefinition[] definitions = ItemTypeCatalogGenerator.Generate(source);

        ItemTypeCatalogFileWriter.Write(destinationPath, definitions);
        PrintResult("item type", definitions.Length, sourcePayload, destinationPath);

        return SuccessExitCode;
    }

    private static int GenerateMapDefinitions(string sourcePath, string destinationPath)
    {
        byte[] sourcePayload = ToolSourceFileReader.Read(sourcePath, MapDefinitionSourceReader.MaximumSourceLengthBytes);
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(sourcePayload);

        MapDefinitionCatalogFileWriter.Write(destinationPath, definitions);
        PrintResult("map", definitions.Length, sourcePayload, destinationPath);

        return SuccessExitCode;
    }

    private static int GenerateBaseTerrain(string sourcePath, string destinationPath)
    {
        MapTerrainLoadLimits limits = MapTerrainLoadLimits.CreateDefault();
        byte[] sourcePayload = ToolSourceFileReader.Read(sourcePath, checked((int)limits.MaximumContainerBytes));
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(sourcePayload, limits);

        MapBaseTerrainBinaryWriter.Write(destinationPath, terrain);

        Console.WriteLine($"Generated base terrain for MapDataId {terrain.MapDataId}.");
        Console.WriteLine($"Source SHA-256: {ComputeSha256(sourcePayload)}");
        Console.WriteLine($"Terrain SHA-256: {ComputeSha256(destinationPath)}");

        return SuccessExitCode;
    }

    private static void PrintResult(string contentName, int count, ReadOnlySpan<byte> sourcePayload, string destinationPath)
    {
        Console.WriteLine($"Generated {count} {contentName} definitions.");
        Console.WriteLine($"Source SHA-256: {ComputeSha256(sourcePayload)}");
        Console.WriteLine($"Catalog SHA-256: {ComputeSha256(destinationPath)}");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool generate-item-types <itemtype.dat> <item-types.json>");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool generate-map-definitions <source.json> <map-definitions.json>");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool generate-base-terrain <source.json> <terrain.ocbt>");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool generate-map-terrains <client-root> <map-definitions.json> <destination-directory>");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool import-map-catalog <source.json> <new-destination-directory>");
        Console.Error.WriteLine("  OpenConquer.GameData.Tool verify-map-collision-sources <client-root> <map-definitions.json> <terrain-directory>");
    }

    private static string ComputeSha256(ReadOnlySpan<byte> payload)
    {
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
