using System.Text.Json;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapDefinitionSourceReader
{
    public const int FormatVersion = 1;
    public const int MaximumSourceLengthBytes = 1024 * 1024;
    public const int MaximumDefinitions = 10_000;

    public static MapDefinition[] Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length == 0 || payload.Length > MaximumSourceLengthBytes)
        {
            throw new InvalidDataException($"Map-definition source must contain between 1 and {MaximumSourceLengthBytes} bytes.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 5 });
            JsonElement root = document.RootElement;

            ValidateProperties(root, "formatVersion", "maps");

            JsonElement formatVersion = root.GetProperty("formatVersion");

            if (formatVersion.ValueKind != JsonValueKind.Number || !formatVersion.TryGetInt32(out int version))
            {
                throw new InvalidDataException("Map-definition source formatVersion must be an integer.");
            }

            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported map-definition source format version {version}.");
            }

            JsonElement records = root.GetProperty("maps");

            if (records.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Map-definition source maps property must be an array.");
            }

            int count = records.GetArrayLength();

            if (count == 0 || count > MaximumDefinitions)
            {
                throw new InvalidDataException($"Map-definition source must contain between 1 and {MaximumDefinitions} records.");
            }

            MapDefinition[] definitions = new MapDefinition[count];
            HashSet<uint> observedMapIds = [];
            int index = 0;

            foreach (JsonElement record in records.EnumerateArray())
            {
                ValidateProperties(record, "mapId", "mapDataId", "flags", "mapDataEvidence", "flagsEvidence");

                uint mapId = ReadUInt32(record, "mapId");
                uint mapDataId = ReadUInt32(record, "mapDataId");
                ulong flags = ReadUInt64(record, "flags");

                ValidateEvidence(record, "mapDataEvidence");
                ValidateEvidence(record, "flagsEvidence");

                if (!observedMapIds.Add(mapId))
                {
                    throw new InvalidDataException($"Map-definition source contains duplicate world-map ID {mapId}.");
                }

                try
                {
                    definitions[index] = new MapDefinition(mapId, mapDataId, flags);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Map-definition source record {index} contains invalid identity.", exception);
                }

                index++;
            }

            Array.Sort(definitions, static (left, right) => left.MapId.CompareTo(right.MapId));

            return definitions;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Map-definition source contains malformed JSON.", exception);
        }
    }

    private static void ValidateProperties(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Map-definition source requires JSON objects.");
        }

        HashSet<string> observed = new(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !observed.Add(property.Name))
            {
                throw new InvalidDataException($"Map-definition source contains unexpected or duplicate property '{property.Name}'.");
            }
        }

        foreach (string name in required)
        {
            if (!observed.Contains(name))
            {
                throw new InvalidDataException($"Map-definition source is missing property '{name}'.");
            }
        }
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint number))
        {
            throw new InvalidDataException($"Map-definition source property '{name}' must be an unsigned 32-bit integer.");
        }

        return number;
    }

    private static ulong ReadUInt64(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong number))
        {
            throw new InvalidDataException($"Map-definition source property '{name}' must be an unsigned 64-bit integer.");
        }

        return number;
    }

    private static void ValidateEvidence(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Map-definition source property '{name}' must be a string.");
        }

        string? evidence = value.GetString();

        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 512)
        {
            throw new InvalidDataException($"Map-definition source property '{name}' must contain a nonempty evidence reference of at most 512 characters.");
        }
    }
}
