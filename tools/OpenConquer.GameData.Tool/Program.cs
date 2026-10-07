using System.Security.Cryptography;
using OpenConquer.Assets.Items;
using OpenConquer.Domain.Items;
using OpenConquer.GameData.Tool.Items;

namespace OpenConquer.GameData.Tool;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int OperationFailedExitCode = 1;
    private const int InvalidArgumentsExitCode = 2;

    private static int Main(string[] args)
    {
        if (args.Length != 3 || !string.Equals(args[0], "generate-item-types", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: OpenConquer.GameData.Tool generate-item-types <itemtype.dat> <item-types.json>");
            return InvalidArgumentsExitCode;
        }

        try
        {
            string sourcePath = Path.GetFullPath(args[1]);
            string destinationPath = Path.GetFullPath(args[2]);
            StringComparison pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(sourcePath, destinationPath, pathComparison))
            {
                Console.Error.WriteLine("OpenConquer.GameData.Tool: source and destination paths must be different files.");
                return InvalidArgumentsExitCode;
            }

            byte[] sourcePayload = File.ReadAllBytes(sourcePath);
            ItemTypeDatTable source = ItemTypeDatTable.Parse(sourcePayload);
            ItemTypeDefinition[] definitions = ItemTypeCatalogGenerator.Generate(source);

            ItemTypeCatalogFileWriter.Write(destinationPath, definitions);

            Console.WriteLine($"Generated {definitions.Length} item type definitions.");
            Console.WriteLine($"Source SHA-256: {ComputeSha256(sourcePayload)}");
            Console.WriteLine($"Catalog SHA-256: {ComputeSha256(destinationPath)}");

            return SuccessExitCode;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            Console.Error.WriteLine($"OpenConquer.GameData.Tool: {exception.Message}");
            return OperationFailedExitCode;
        }
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
