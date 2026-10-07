namespace OpenConquer.Infrastructure.Content.Items;

public sealed class ItemTypeCatalogFileOptions
{
    public ItemTypeCatalogFileOptions(string filePath, int maximumFileLengthBytes, int maximumDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (maximumFileLengthBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileLengthBytes), maximumFileLengthBytes, "Maximum item-type catalog file length must be positive.");
        }

        if (maximumDefinitions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDefinitions), maximumDefinitions, "Maximum item-type definition count must be positive.");
        }

        FilePath = Path.GetFullPath(filePath);
        MaximumFileLengthBytes = maximumFileLengthBytes;
        MaximumDefinitions = maximumDefinitions;
    }

    public string FilePath
    {
        get;
    }

    public int MaximumFileLengthBytes
    {
        get;
    }

    public int MaximumDefinitions
    {
        get;
    }
}
