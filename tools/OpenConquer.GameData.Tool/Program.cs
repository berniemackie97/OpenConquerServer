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

    private static int Main(string[] args)
    {
        bool validArguments =
            args.Length == 3
                && args[0]
                    is (
                        "generate-item-types"
                        or "generate-map-definitions"
                        or "generate-base-terrain"
                    )
            || args.Length == 4 && args[0] == "generate-map-terrains";

        if (!validArguments)
        {
            PrintUsage();
            return InvalidArgumentsExitCode;
        }

        try
        {
            if (args[0] == "generate-map-terrains")
            {
                string assetRoot = Path.GetFullPath(args[1]);
                string definitionsPath = Path.GetFullPath(args[2]);
                string destinationDirectory = Path.GetFullPath(args[3]);

                int count = NativeMapTerrainImporter.Generate(
                    assetRoot,
                    definitionsPath,
                    destinationDirectory,
                    MapTerrainLoadLimits.CreateDefault()
                );

                Console.WriteLine($"Generated {count} canonical base-terrain artifacts.");

                return SuccessExitCode;
            }

            string sourcePath = Path.GetFullPath(args[1]);
            string destinationPath = Path.GetFullPath(args[2]);
            StringComparison pathComparison =
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

            if (string.Equals(sourcePath, destinationPath, pathComparison))
            {
                Console.Error.WriteLine(
                    "OpenConquer.GameData.Tool: source and destination paths must be different files."
                );
                return InvalidArgumentsExitCode;
            }

            return args[0] switch
            {
                "generate-item-types" => GenerateItemTypes(sourcePath, destinationPath),
                "generate-map-definitions" => GenerateMapDefinitions(sourcePath, destinationPath),
                "generate-base-terrain" => GenerateBaseTerrain(sourcePath, destinationPath),
                _ => InvalidArgumentsExitCode,
            };
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            Console.Error.WriteLine($"OpenConquer.GameData.Tool: {exception.Message}");
            return OperationFailedExitCode;
        }
    }

    private static int GenerateItemTypes(string sourcePath, string destinationPath)
    {
        byte[] sourcePayload = ToolSourceFileReader.Read(
            sourcePath,
            MaximumItemTypeSourceLengthBytes
        );
        ItemTypeDatTable source = ItemTypeDatTable.Parse(sourcePayload);
        ItemTypeDefinition[] definitions = ItemTypeCatalogGenerator.Generate(source);

        ItemTypeCatalogFileWriter.Write(destinationPath, definitions);

        PrintResult("item type", definitions.Length, sourcePayload, destinationPath);

        return SuccessExitCode;
    }

    private static int GenerateMapDefinitions(string sourcePath, string destinationPath)
    {
        byte[] sourcePayload = ToolSourceFileReader.Read(
            sourcePath,
            MapDefinitionSourceReader.MaximumSourceLengthBytes
        );
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(sourcePayload);

        MapDefinitionCatalogFileWriter.Write(destinationPath, definitions);

        PrintResult("map", definitions.Length, sourcePayload, destinationPath);

        return SuccessExitCode;
    }

    private static int GenerateBaseTerrain(string sourcePath, string destinationPath)
    {
        MapTerrainLoadLimits limits = MapTerrainLoadLimits.CreateDefault();
        byte[] sourcePayload = ToolSourceFileReader.Read(
            sourcePath,
            checked((int)limits.MaximumContainerBytes)
        );
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(sourcePayload, limits);

        MapBaseTerrainBinaryWriter.Write(destinationPath, terrain);

        Console.WriteLine($"Generated base terrain for MapDataId {terrain.MapDataId}.");
        Console.WriteLine($"Source SHA-256: {ComputeSha256(sourcePayload)}");
        Console.WriteLine($"Terrain SHA-256: {ComputeSha256(destinationPath)}");

        return SuccessExitCode;
    }

    private static void PrintResult(
        string contentName,
        int count,
        ReadOnlySpan<byte> sourcePayload,
        string destinationPath
    )
    {
        Console.WriteLine($"Generated {count} {contentName} definitions.");
        Console.WriteLine($"Source SHA-256: {ComputeSha256(sourcePayload)}");
        Console.WriteLine($"Catalog SHA-256: {ComputeSha256(destinationPath)}");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine(
            "  OpenConquer.GameData.Tool generate-item-types <itemtype.dat> <item-types.json>"
        );
        Console.Error.WriteLine(
            "  OpenConquer.GameData.Tool generate-map-definitions <source.json> <map-definitions.json>"
        );
        Console.Error.WriteLine(
            "  OpenConquer.GameData.Tool generate-base-terrain <source.json> <terrain.ocbt>"
        );
        Console.Error.WriteLine(
            "  OpenConquer.GameData.Tool generate-map-terrains <client-root> <map-definitions.json> <destination-directory>"
        );
    }

    private static string ComputeSha256(ReadOnlySpan<byte> payload)
    {
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan
        );

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
