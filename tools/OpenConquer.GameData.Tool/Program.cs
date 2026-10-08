using System.Security.Cryptography;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Items;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Items;
using OpenConquer.GameData.Tool.Maps;

namespace OpenConquer.GameData.Tool;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int OperationFailedExitCode = 1;
    private const int InvalidArgumentsExitCode = 2;

    private static int Main(string[] args)
    {
        if (
            args.Length != 3
            || args[0] is not ("generate-item-types" or "generate-map-definitions")
        )
        {
            PrintUsage();
            return InvalidArgumentsExitCode;
        }

        try
        {
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
        byte[] sourcePayload = File.ReadAllBytes(sourcePath);
        ItemTypeDatTable source = ItemTypeDatTable.Parse(sourcePayload);
        ItemTypeDefinition[] definitions = ItemTypeCatalogGenerator.Generate(source);

        ItemTypeCatalogFileWriter.Write(destinationPath, definitions);

        PrintResult("item type", definitions.Length, sourcePayload, destinationPath);

        return SuccessExitCode;
    }

    private static int GenerateMapDefinitions(string sourcePath, string destinationPath)
    {
        FileInfo sourceFile = new(sourcePath);

        if (sourceFile.Length > MapDefinitionSourceReader.MaximumSourceLengthBytes)
        {
            throw new InvalidDataException(
                $"Map-definition source exceeds {MapDefinitionSourceReader.MaximumSourceLengthBytes} bytes."
            );
        }

        byte[] sourcePayload = File.ReadAllBytes(sourcePath);
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(sourcePayload);

        MapDefinitionCatalogFileWriter.Write(destinationPath, definitions);

        PrintResult("map definition", definitions.Length, sourcePayload, destinationPath);

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
            "  OpenConquer.GameData.Tool generate-map-definitions <map-definitions.source.json> <map-definitions.json>"
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
