using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;

namespace OpenConquer.Infrastructure.Content.Maps;

public sealed class FileMapTransitionCatalogRepository : IMapTransitionCatalogRepository
{
    public const int FormatVersion = 1;

    private readonly string _filePath;
    private readonly int _maximumFileLengthBytes;
    private readonly int _maximumEntries;

    public FileMapTransitionCatalogRepository(string filePath, int maximumFileLengthBytes, int maximumEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileLengthBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);

        _filePath = Path.GetFullPath(filePath);
        _maximumFileLengthBytes = maximumFileLengthBytes;
        _maximumEntries = maximumEntries;
    }

    public async ValueTask<MapTransitionCatalog> LoadAsync(WorldMapContentCatalog maps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(maps);
        cancellationToken.ThrowIfCancellationRequested();

        FileAttributes attributes = File.GetAttributes(_filePath);
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException($"Map-transition catalog path '{_filePath}' is a directory.");
        }

        FileInfo file = new(_filePath);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
        {
            throw new IOException($"Map-transition catalog path '{_filePath}' is a symbolic link or reparse point.");
        }

        await using FileStream stream = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

        long length = stream.Length;
        if (length == 0 || length > _maximumFileLengthBytes)
        {
            throw new InvalidDataException($"Map-transition catalog file length {length} is outside the configured bounds.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)length));
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        byte[] trailing = new byte[1];
        if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Map-transition catalog file changed while being read.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            ValidateProperties(root, "formatVersion", "transitions");

            if (ReadInt32(root, "formatVersion") != FormatVersion)
            {
                throw new InvalidDataException("Unsupported map-transition catalog format version.");
            }

            JsonElement rows = root.GetProperty("transitions");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > _maximumEntries)
            {
                throw new InvalidDataException("Map-transition catalog transitions must be a bounded array.");
            }

            List<MapTransitionEntry> entries = new(rows.GetArrayLength());
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Map-transition catalog contains a non-object entry.");
                }

                string status = ReadString(row, "status");
                MapTransitionDestination? destination = null;
                if (status is "resolved" or "release-approved")
                {
                    ValidateProperties(row, "mapId", "passwayIndex", "status", "source", "targetMapId", "targetPortalIndex", "targetX", "targetY");
                    destination = new MapTransitionDestination(ReadUInt32(row, "targetMapId"), ReadUInt32(row, "targetPortalIndex"),
                        ReadInt32(row, "targetX"), ReadInt32(row, "targetY"), ReadString(row, "source"));
                }
                else
                {
                    ValidateProperties(row, "mapId", "passwayIndex", "status");
                }

                entries.Add(new MapTransitionEntry(ReadUInt32(row, "mapId"), ReadUInt32(row, "passwayIndex"), status, destination));
            }

            return new MapTransitionCatalog(entries, maps);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Map-transition catalog JSON is malformed.", exception);
        }
    }

    private static void ValidateProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Map-transition catalog requires an object.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                throw new InvalidDataException($"Unexpected or duplicate map-transition property '{property.Name}'.");
            }
        }

        if (seen.Count != expected.Length)
        {
            throw new InvalidDataException("Map-transition catalog is missing a required property.");
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"Map-transition property '{name}' is missing.");
        }

        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text : throw new InvalidDataException($"Map-transition property '{name}' must be a string.");
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"Map-transition property '{name}' is missing.");
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number : throw new InvalidDataException($"Map-transition property '{name}' must be a signed integer.");
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException($"Map-transition property '{name}' is missing.");
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint number)
            ? number : throw new InvalidDataException($"Map-transition property '{name}' must be an unsigned integer.");
    }
}
