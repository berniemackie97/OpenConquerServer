namespace OpenConquer.Infrastructure.Content.Maps;

public sealed class MapDefinitionCatalogFileOptions
{
    public MapDefinitionCatalogFileOptions(string filePath, int maximumFileLengthBytes, int maximumDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (maximumFileLengthBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileLengthBytes), maximumFileLengthBytes,
                "Maximum map-definition catalog file length must be positive.");
        }

        if (maximumDefinitions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDefinitions), maximumDefinitions,
                "Maximum map-definition count must be positive.");
        }

        FilePath = Path.GetFullPath(filePath);
        MaximumFileLengthBytes = maximumFileLengthBytes;
        MaximumDefinitions = maximumDefinitions;
    }

    public string FilePath { get; }
    public int MaximumFileLengthBytes { get; }
    public int MaximumDefinitions { get; }
}
