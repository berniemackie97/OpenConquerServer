using System.Globalization;
using System.Text.Json;

namespace OpenConquer.GameData.Tool.Maps;

internal sealed record MapCatalogSourceEntry(uint MapId, uint? MapDataId, ulong? SourceFlags,
    ulong? ClientFlags, string Status, string Confidence, string MapDataSource,
    string FlagsBasis, string Reason, string? TerrainPath);

internal static class MapCatalogSourceReader
{
    public const int MaximumSourceLengthBytes = 8 * 1024 * 1024;
    private const int MaximumRecords = 10_000;
    private const ulong ClientFlagMask = (1UL << 41) - 1;

    public static MapCatalogSourceEntry[] Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length is 0 or > MaximumSourceLengthBytes)
        {
            throw new InvalidDataException("Map catalog source has an invalid length.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rows", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is 0 or > MaximumRecords)
            {
                throw new InvalidDataException("Map catalog source requires a nonempty bounded rows array.");
            }

            List<MapCatalogSourceEntry> result = new(rows.GetArrayLength());
            HashSet<uint> observed = [];

            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Map catalog source contains a non-object record.");
                }

                HashSet<string> properties = new(StringComparer.Ordinal);

                foreach (JsonProperty property in row.EnumerateObject())
                {
                    if (!properties.Add(property.Name))
                    {
                        throw new InvalidDataException($"Map catalog source contains duplicate property '{property.Name}'.");
                    }
                }

                uint mapId = ReadUInt32(row, "map_id");

                if (mapId == 0 || !observed.Add(mapId))
                {
                    throw new InvalidDataException($"Map catalog source contains invalid or duplicate map ID {mapId}.");
                }

                string status = ReadString(row, "status");
                string confidence = ReadString(row, "confidence");
                string mapDataSource = ReadString(row, "map_data_source");
                string flagsBasis = ReadString(row, "flags_basis");
                string reason = ReadString(row, "reason", 1024);
                ulong? sourceFlags = ReadNullableUInt64(row, "flags_source_value");

                if (status == "unavailable")
                {
                    if (row.GetProperty("map_data_id").ValueKind != JsonValueKind.Null
                        || sourceFlags is not null || confidence != "U"
                        || row.TryGetProperty("flags_5517", out JsonElement unavailableFlags)
                            && unavailableFlags.ValueKind != JsonValueKind.Null)
                    {
                        throw new InvalidDataException($"Unavailable map {mapId} contains published content.");
                    }

                    result.Add(new MapCatalogSourceEntry(mapId, null, null, null, status,
                        confidence, mapDataSource, flagsBasis, reason, null));
                    continue;
                }

                if (status != "resolved" || confidence is not ("A" or "B" or "C" or "D"))
                {
                    throw new InvalidDataException($"Map {mapId} has invalid availability or confidence.");
                }

                uint mapDataId = ReadUInt32(row, "map_data_id");

                if (mapDataId == 0)
                {
                    throw new InvalidDataException($"Map {mapId} has no terrain identity.");
                }

                string terrainPath = ReadString(row.GetProperty("terrain"), "path");
                ulong clientFlags = ReadNullableUInt64(row, "flags_5517")
                    ?? throw new InvalidDataException($"Map {mapId} has no client flag value.");

                if (sourceFlags is null)
                {
                    if (flagsBasis != "DEFAULT_ZERO_NO_SOURCE" || clientFlags != 0)
                    {
                        throw new InvalidDataException($"Map {mapId} has an undocumented flag default.");
                    }
                }
                else if (flagsBasis == "DEFAULT_ZERO_NO_SOURCE"
                    || clientFlags != (sourceFlags.Value & ClientFlagMask))
                {
                    throw new InvalidDataException($"Map {mapId} has inconsistent source and client flags.");
                }

                ValidateHex(row, "flags_source_hex", sourceFlags);
                ValidateHex(row, "flags_5517_hex", clientFlags);

                result.Add(new MapCatalogSourceEntry(mapId, mapDataId, sourceFlags,
                    clientFlags, status, confidence, mapDataSource, flagsBasis,
                    reason, terrainPath));
            }

            result.Sort(static (left, right) => left.MapId.CompareTo(right.MapId));
            return result.ToArray();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Map catalog source contains malformed or incomplete data.", exception);
        }
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint result))
        {
            throw new InvalidDataException($"Map catalog property '{name}' must be an unsigned 32-bit integer.");
        }

        return result;
    }

    private static string ReadString(JsonElement element, string name, int maximumLength = 512)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Map catalog property '{name}' must be a string.");
        }

        string? result = value.GetString();

        if (string.IsNullOrWhiteSpace(result) || result.Length > maximumLength)
        {
            throw new InvalidDataException($"Map catalog property '{name}' has an invalid value.");
        }

        return result;
    }

    private static ulong? ReadNullableUInt64(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong result))
        {
            throw new InvalidDataException($"Map catalog property '{name}' must be a decimal unsigned 64-bit value.");
        }

        return result;
    }

    private static void ValidateHex(JsonElement element, string name, ulong? expected)
    {
        JsonElement value = element.GetProperty(name);

        if (expected is null && value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Map catalog property '{name}' has an invalid hex value.");
        }

        string? text = value.GetString();

        if (text is null || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong actual)
            || actual != expected)
        {
            throw new InvalidDataException($"Map catalog property '{name}' disagrees with its decimal value.");
        }
    }
}
