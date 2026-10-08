using System.Text.Json;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapDefinitionCatalogFileWriter
{
    public const int FormatVersion = 1;

    public static void Write(string destinationPath, IReadOnlyList<MapDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Count == 0)
        {
            throw new ArgumentException("Map-definition catalog cannot be empty.", nameof(definitions));
        }

        MapDefinition[] ordered = definitions.ToArray();
        HashSet<uint> observedMapIds = [];

        foreach (MapDefinition definition in ordered)
        {
            if (definition is null)
            {
                throw new ArgumentException("Map-definition catalog cannot contain null definitions.", nameof(definitions));
            }

            if (!observedMapIds.Add(definition.MapId))
            {
                throw new ArgumentException($"Map-definition catalog contains duplicate world-map ID {definition.MapId}.", nameof(definitions));
            }
        }

        Array.Sort(ordered, static (left, right) => left.MapId.CompareTo(right.MapId));

        string fullPath = Path.GetFullPath(destinationPath);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Map-definition catalog destination must have a parent directory.", nameof(destinationPath));

        Directory.CreateDirectory(directoryPath);

        string temporaryPath = Path.Combine(directoryPath, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.SequentialScan))
            {
                using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("formatVersion", FormatVersion);
                    writer.WritePropertyName("maps");
                    writer.WriteStartArray();

                    foreach (MapDefinition definition in ordered)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("mapId", definition.MapId);
                        writer.WriteNumber("mapDataId", definition.MapDataId);
                        writer.WriteNumber("flags", definition.Flags);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
