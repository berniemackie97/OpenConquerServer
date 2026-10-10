using System.Security.Cryptography;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapCatalogImporter
{
    public static (int Resolved, int Unavailable) Import(byte[] sourcePayload, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourcePayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        MapCatalogSourceEntry[] entries = MapCatalogSourceReader.Parse(sourcePayload);

        MapDefinition[] definitions = entries
            .Where(entry => entry.Status == "resolved")
            .Select(entry => new MapDefinition(entry.MapId, entry.MapDataId!.Value, entry.SourceFlags ?? 0))
            .ToArray();

        if (definitions.Length == 0)
        {
            throw new InvalidDataException("Map catalog import contains no resolved maps.");
        }

        string destination = Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Map catalog destination '{destination}' already exists.");
        }

        string parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("Map catalog destination requires a parent directory.");

        Directory.CreateDirectory(parent);

        string staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);

        try
        {
            MapDefinitionCatalogFileWriter.Write(Path.Combine(staging, "map-definitions.json"), definitions);

            string sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(sourcePayload));

            MapCatalogManifestWriter.Write(Path.Combine(staging, "catalog-manifest.json"),
                entries, sourceSha256);

            Directory.Move(staging, destination);

            return (definitions.Length, entries.Length - definitions.Length);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
