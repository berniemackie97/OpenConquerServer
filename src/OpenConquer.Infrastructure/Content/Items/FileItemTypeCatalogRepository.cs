using System.Text.Json;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.Domain.Items;

namespace OpenConquer.Infrastructure.Content.Items;

public sealed class FileItemTypeCatalogRepository(ItemTypeCatalogFileOptions options) : IItemTypeCatalogRepository
{
    public const int FormatVersion = 1;

    private readonly ItemTypeCatalogFileOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async ValueTask<ItemTypeCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] payload = await ReadPayloadAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        ItemTypeCatalogFileDocument document;

        try
        {
            using JsonDocument json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 5 });
            JsonElement root = json.RootElement;

            ValidateUniqueProperties(root, "root");

            if (root.TryGetProperty("itemTypes", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array)
            {
                int index = 0;

                foreach (JsonElement entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object)
                    {
                        ValidateUniqueProperties(entry, $"itemTypes[{index}]");
                    }

                    index++;
                }
            }

            document = JsonSerializer.Deserialize<ItemTypeCatalogFileDocument>(root)
                ?? throw new InvalidDataException("Item-type catalog content is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Item-type catalog content is malformed.", exception);
        }

        if (document.FormatVersion != FormatVersion)
        {
            throw new InvalidDataException($"Item-type catalog format version {document.FormatVersion} is unsupported. Expected version {FormatVersion}.");
        }

        if (document.ItemTypes is null)
        {
            throw new InvalidDataException("Item-type catalog content is missing its itemTypes collection.");
        }

        if (document.ItemTypes.Length > _options.MaximumDefinitions)
        {
            throw new InvalidDataException($"Item-type catalog contains {document.ItemTypes.Length} definitions, exceeding the configured maximum of {_options.MaximumDefinitions}.");
        }

        ItemTypeDefinition[] definitions = new ItemTypeDefinition[document.ItemTypes.Length];

        for (int index = 0; index < document.ItemTypes.Length; index++)
        {
            ItemTypeCatalogFileEntry entry = document.ItemTypes[index]
                ?? throw new InvalidDataException($"Item-type catalog entry {index} is null.");

            try
            {
                definitions[index] = new ItemTypeDefinition(entry.ItemTypeId, entry.Name, entry.RequiredLevel,
                    entry.SpeedPercentOffset, entry.Life, entry.Mana, entry.InitialDurability, entry.MaximumDurability,
                    entry.StaticLifetimeMinutes, entry.StackCapacity);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Item-type catalog entry {index} for item type {entry.ItemTypeId} contains invalid state.", exception);
            }
        }

        try
        {
            return new ItemTypeCatalog(definitions);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Item-type catalog content contains invalid aggregate state.", exception);
        }
    }

    private static void ValidateUniqueProperties(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Item-type catalog {context} must be a JSON object.");
        }

        HashSet<string> observed = new(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!observed.Add(property.Name))
            {
                throw new InvalidDataException($"Item-type catalog {context} contains duplicate JSON property '{property.Name}'.");
            }
        }
    }

    private async ValueTask<byte[]> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(_options.FilePath);
        }
        catch (FileNotFoundException exception)
        {
            throw new FileNotFoundException($"Item-type catalog file '{_options.FilePath}' does not exist.", _options.FilePath, exception);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException($"Item-type catalog path '{_options.FilePath}' is a directory.");
        }

        FileInfo file = new(_options.FilePath);

        if ((attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
        {
            throw new IOException($"Item-type catalog file '{_options.FilePath}' is a symbolic link or reparse point.");
        }

        long fileLength = file.Length;

        if (fileLength == 0)
        {
            throw new InvalidDataException("Item-type catalog file is empty.");
        }

        if (fileLength > _options.MaximumFileLengthBytes)
        {
            throw new InvalidDataException($"Item-type catalog file length {fileLength} exceeds the configured maximum of {_options.MaximumFileLengthBytes} bytes.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)fileLength));

        await using FileStream stream = new(_options.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

        int offset = 0;

        while (offset < payload.Length)
        {
            int read = await stream.ReadAsync(payload.AsMemory(offset), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new InvalidDataException("Item-type catalog file changed while it was being read.");
            }

            offset += read;
        }

        byte[] trailingByte = new byte[1];

        if (await stream.ReadAsync(trailingByte, cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("Item-type catalog file changed while it was being read.");
        }

        return payload;
    }
}
