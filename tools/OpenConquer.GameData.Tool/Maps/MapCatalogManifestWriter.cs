using System.Globalization;
using System.Text.Json;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapCatalogManifestWriter
{
    public const int FormatVersion = 1;

    public static void Write(string destinationPath, IReadOnlyList<MapCatalogSourceEntry> entries, string sourceSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);

        using FileStream stream = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("sourceSha256", sourceSha256);
        writer.WritePropertyName("maps");
        writer.WriteStartArray();

        foreach (MapCatalogSourceEntry entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteNumber("mapId", entry.MapId);
            writer.WriteString("status", entry.Status);

            if (entry.MapDataId is uint mapDataId)
            {
                writer.WriteNumber("mapDataId", mapDataId);
            }
            else
            {
                writer.WriteNull("mapDataId");
            }

            writer.WriteString("confidence", entry.Confidence);
            writer.WriteString("mapDataSource", entry.MapDataSource);
            writer.WriteString("flagsBasis", entry.FlagsBasis);
            writer.WriteString("reason", entry.Reason);
            writer.WriteString("terrainPath", entry.TerrainPath);
            writer.WriteString("flagsSourceValue", entry.SourceFlags?.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("clientFlags", entry.ClientFlags?.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
