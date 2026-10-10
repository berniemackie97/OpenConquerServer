using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapBaseTerrainSourceReader
{
    public const int FormatVersion = 1;

    public static MapBaseTerrain Parse(byte[] payload, MapTerrainLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(limits);

        if (payload.Length == 0 || payload.Length > limits.MaximumContainerBytes)
        {
            throw new InvalidDataException("Terrain authoring source is empty or exceeds the configured source limit.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 6 });
            JsonElement root = document.RootElement;

            ValidateProperties(root, "formatVersion", "mapDataId", "width", "height", "cells", "exits");

            int version = ReadInt32(root, "formatVersion");

            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported terrain authoring format version {version}.");
            }

            uint mapDataId = ReadUInt32(root, "mapDataId");
            int width = ReadInt32(root, "width");
            int height = ReadInt32(root, "height");

            if (mapDataId == 0 || width <= 0 || height <= 0
                || width > limits.MaximumWidth || height > limits.MaximumHeight)
            {
                throw new InvalidDataException("Terrain authoring source contains invalid identity or dimensions.");
            }

            long expectedCells = (long)width * height;

            if (expectedCells > limits.MaximumCellsPerTerrain)
            {
                throw new InvalidDataException("Terrain authoring source exceeds the configured cell capacity.");
            }

            JsonElement cellEntries = root.GetProperty("cells");
            JsonElement exitEntries = root.GetProperty("exits");

            if (cellEntries.ValueKind != JsonValueKind.Array || exitEntries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Terrain cells and exits must be JSON arrays.");
            }

            if (cellEntries.GetArrayLength() != expectedCells
                || exitEntries.GetArrayLength() > limits.MaximumExitsPerTerrain)
            {
                throw new InvalidDataException("Terrain authoring source contains an invalid cell or exit count.");
            }

            MapBaseTerrainCell[] cells = new MapBaseTerrainCell[checked((int)expectedCells)];
            MapTerrainExit[] exits = new MapTerrainExit[exitEntries.GetArrayLength()];
            int index = 0;

            foreach (JsonElement entry in cellEntries.EnumerateArray())
            {
                ValidateProperties(entry, "surfaceId", "passabilityFlag", "elevation");

                cells[index++] = new MapBaseTerrainCell(
                    ReadUInt16(entry, "surfaceId"),
                    ReadUInt16(entry, "passabilityFlag"),
                    ReadInt16(entry, "elevation"));
            }

            index = 0;

            foreach (JsonElement entry in exitEntries.EnumerateArray())
            {
                ValidateProperties(entry, "x", "y", "passwayIndex");

                exits[index++] = new MapTerrainExit(
                    ReadInt32(entry, "x"),
                    ReadInt32(entry, "y"),
                    ReadUInt32(entry, "passwayIndex"));
            }

            return new MapBaseTerrain(mapDataId, width, height, cells, exits);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Terrain authoring source contains malformed JSON.", exception);
        }
    }

    private static void ValidateProperties(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Terrain authoring source requires JSON objects.");
        }

        HashSet<string> observed = new(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !observed.Add(property.Name))
            {
                throw new InvalidDataException($"Terrain authoring source contains unexpected or duplicate property '{property.Name}'.");
            }
        }

        foreach (string name in required)
        {
            if (!observed.Contains(name))
            {
                throw new InvalidDataException($"Terrain authoring source is missing property '{name}'.");
            }
        }
    }

    private static ushort ReadUInt16(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt16(out ushort result))
        {
            throw new InvalidDataException($"Terrain property '{name}' must be an unsigned 16-bit integer.");
        }

        return result;
    }

    private static short ReadInt16(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt16(out short result))
        {
            throw new InvalidDataException($"Terrain property '{name}' must be a signed 16-bit integer.");
        }

        return result;
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint result))
        {
            throw new InvalidDataException($"Terrain property '{name}' must be an unsigned 32-bit integer.");
        }

        return result;
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new InvalidDataException($"Terrain property '{name}' must be a signed 32-bit integer.");
        }

        return result;
    }
}
