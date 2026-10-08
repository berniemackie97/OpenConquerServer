using System.Text.Json;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public sealed class FileMapDefinitionCatalogRepository(MapDefinitionCatalogFileOptions options) : IMapDefinitionCatalogRepository
{
    public const int FormatVersion = 1;

    private readonly MapDefinitionCatalogFileOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] payload = await ReadPayloadAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 5 });
            JsonElement root = document.RootElement;

            ValidateProperties(root, "formatVersion", "maps");

            int version = ReadInt32(root, "formatVersion");

            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Map-definition catalog format version {version} is unsupported. Expected version {FormatVersion}.");
            }

            JsonElement entries = root.GetProperty("maps");

            if (entries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Map-definition catalog maps property must be an array.");
            }

            int count = entries.GetArrayLength();

            if (count == 0)
            {
                throw new InvalidDataException("Map-definition catalog cannot be empty.");
            }

            if (count > _options.MaximumDefinitions)
            {
                throw new InvalidDataException($"Map-definition catalog contains {count} definitions, exceeding the configured maximum of {_options.MaximumDefinitions}.");
            }

            MapDefinition[] definitions = new MapDefinition[count];
            int index = 0;

            foreach (JsonElement entry in entries.EnumerateArray())
            {
                ValidateProperties(entry, "mapId", "mapDataId", "flags");

                uint mapId = ReadUInt32(entry, "mapId");
                uint mapDataId = ReadUInt32(entry, "mapDataId");
                ulong flags = ReadUInt64(entry, "flags");

                try
                {
                    definitions[index] = new MapDefinition(mapId, mapDataId, flags);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Map-definition catalog entry {index} contains invalid map identity.", exception);
                }

                index++;
            }

            try
            {
                return new MapDefinitionCatalog(definitions);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Map-definition catalog contains invalid aggregate state.", exception);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Map-definition catalog content is malformed.", exception);
        }
    }

    private async ValueTask<byte[]> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        FileAttributes attributes = File.GetAttributes(_options.FilePath);

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException($"Map-definition catalog path '{_options.FilePath}' is a directory.");
        }

        FileInfo file = new(_options.FilePath);

        if ((attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
        {
            throw new IOException($"Map-definition catalog path '{_options.FilePath}' is a symbolic link or reparse point.");
        }

        await using FileStream stream = new(_options.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

        long length = stream.Length;

        if (length == 0)
        {
            throw new InvalidDataException("Map-definition catalog file is empty.");
        }

        if (length > _options.MaximumFileLengthBytes)
        {
            throw new InvalidDataException($"Map-definition catalog file length {length} exceeds the configured maximum of {_options.MaximumFileLengthBytes} bytes.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)length));

        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        byte[] trailing = new byte[1];

        if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Map-definition catalog file changed while being read.");
        }

        return payload;
    }

    private static void ValidateProperties(JsonElement element, params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Map-definition catalog contains a value where an object is required.");
        }

        HashSet<string> observed = new(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name, StringComparer.Ordinal) || !observed.Add(property.Name))
            {
                throw new InvalidDataException($"Map-definition catalog contains unexpected or duplicate property '{property.Name}'.");
            }
        }

        foreach (string name in expectedProperties)
        {
            if (!observed.Contains(name))
            {
                throw new InvalidDataException($"Map-definition catalog is missing required property '{name}'.");
            }
        }
    }

    private static int ReadInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            throw new InvalidDataException($"Map-definition catalog property '{name}' must be an integer.");
        }

        return number;
    }

    private static uint ReadUInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint number))
        {
            throw new InvalidDataException($"Map-definition catalog property '{name}' must be an unsigned 32-bit integer.");
        }

        return number;
    }

    private static ulong ReadUInt64(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong number))
        {
            throw new InvalidDataException($"Map-definition catalog property '{name}' must be an unsigned 64-bit integer.");
        }

        return number;
    }
}
