using System.Text.Json;
using OpenConquer.Application.World.Revival;

namespace OpenConquer.Infrastructure.Content.Maps;

/// <summary>
/// Strict reader for offline-vetted historical revival evidence. Does not infer missing records.
/// This is source data, not a client packet or a runtime authentication input.
/// </summary>
public static class MapRevivalSourceReader
{
    public const int FormatVersion = 1;
    public const int MaximumPayloadBytes = 1024 * 1024;
    public const int MaximumRecords = 10_000;

    /// <summary>Reads an immutable file using bounded, non-link access before parsing.</summary>
    public static async ValueTask<MapRevivalSource[]> LoadFileAsync(string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        FileAttributes attributes = File.GetAttributes(path);
        FileInfo info = new(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            info.LinkTarget is not null)
        {
            throw new IOException("Revival source must be a regular non-link file.");
        }
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length is < 1 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Revival source length is outside the supported bound.");
        }
        byte[] bytes = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        byte[] sentinel = new byte[1];
        if (await stream.ReadAsync(sentinel, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Revival source changed while being read.");
        }
        return Parse(bytes);
    }

    public static MapRevivalSource[] Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is 0 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Revival source size is empty or exceeds the configured bound.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 5 });
            JsonElement root = document.RootElement;
            ValidateProperties(root, "formatVersion", "maps");

            JsonElement version = root.GetProperty("formatVersion");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int format) ||
                format != FormatVersion)
            {
                throw new InvalidDataException("Unsupported revival-source format version.");
            }

            JsonElement maps = root.GetProperty("maps");
            if (maps.ValueKind != JsonValueKind.Array || maps.GetArrayLength() is 0 or > MaximumRecords)
            {
                throw new InvalidDataException("Revival-source maps must be a nonempty, bounded array.");
            }

            MapRevivalSource[] rows = new MapRevivalSource[maps.GetArrayLength()];
            HashSet<uint> seen = [];
            int index = 0;
            foreach (JsonElement row in maps.EnumerateArray())
            {
                ValidateProperties(row, "mapId", "rebornMapId", "portal0X", "portal0Y", "provenance");
                uint mapId = ReadUInt32(row, "mapId");
                uint reborn = ReadUInt32(row, "rebornMapId");
                uint x = ReadUInt32(row, "portal0X");
                uint y = ReadUInt32(row, "portal0Y");
                JsonElement proof = row.GetProperty("provenance");
                if (mapId == 0 || !seen.Add(mapId) || proof.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Revival-source identity or provenance is invalid.");
                }
                string? provenance = proof.GetString();
                if (string.IsNullOrWhiteSpace(provenance) || provenance.Length > 80 ||
                    provenance.Any(static c => !char.IsLetterOrDigit(c) && c is not ('-' or '_')))
                {
                    throw new InvalidDataException("Revival-source provenance must be a bounded identifier.");
                }
                rows[index++] = new MapRevivalSource(mapId, reborn, x, y, provenance);
            }
            return rows;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Revival-source JSON is malformed.", exception);
        }
    }

    private static uint ReadUInt32(JsonElement row, string name)
    {
        JsonElement property = row.GetProperty(name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetUInt32(out uint number))
        {
            throw new InvalidDataException($"Revival-source '{name}' must be an unsigned integer.");
        }
        return number;
    }

    private static void ValidateProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Revival-source entries must be objects.");
        }
        HashSet<string> observed = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !observed.Add(property.Name))
            {
                throw new InvalidDataException($"Unexpected or duplicate revival-source property '{property.Name}'.");
            }
        }
        if (observed.Count != names.Length)
        {
            throw new InvalidDataException("Revival-source entry is missing a required property.");
        }
    }
}
