using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public sealed class FileMapBaseTerrainCatalogRepository : IMapBaseTerrainCatalogRepository
{
    private readonly string _rootPath;
    private readonly MapTerrainLoadLimits _limits;

    public FileMapBaseTerrainCatalogRepository(string rootPath, MapTerrainLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(limits);

        _rootPath = Path.GetFullPath(rootPath);
        _limits = limits;
    }

    public async ValueTask<MapBaseTerrainCatalog> LoadAsync(MapDefinitionCatalog definitions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo directory = new(_rootPath);

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Canonical terrain directory '{_rootPath}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || directory.LinkTarget is not null)
        {
            throw new IOException($"Canonical terrain directory '{_rootPath}' cannot be a symbolic link.");
        }

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();
        HashSet<string> expectedFiles = requiredIds.Select(id => $"{id}{MapBaseTerrainBinaryReader.FileExtension}").ToHashSet(StringComparer.Ordinal);

        foreach (string entry in Directory.EnumerateFileSystemEntries(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(entry);

            if (!expectedFiles.Remove(fileName))
            {
                throw new InvalidDataException($"Canonical terrain directory contains unexpected entry '{fileName}'.");
            }
        }

        if (expectedFiles.Count != 0)
        {
            string missing = expectedFiles.Order(StringComparer.Ordinal).First();
            throw new FileNotFoundException($"Canonical terrain directory is missing required artifact '{missing}'.", Path.Combine(_rootPath, missing));
        }

        List<MapBaseTerrain> terrains = new(requiredIds.Length);
        long totalCells = 0;

        foreach (uint mapDataId in requiredIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = Path.Combine(_rootPath, $"{mapDataId}{MapBaseTerrainBinaryReader.FileExtension}");
            MapBaseTerrain terrain = await MapBaseTerrainBinaryReader.ReadAsync(path, mapDataId, _limits, cancellationToken).ConfigureAwait(false);

            totalCells = checked(totalCells + terrain.CellCount);

            if (totalCells > _limits.MaximumTotalCells)
            {
                throw new InvalidDataException($"Canonical terrain contains {totalCells} cells, exceeding the configured aggregate limit of {_limits.MaximumTotalCells}.");
            }

            terrains.Add(terrain);
        }

        return new MapBaseTerrainCatalog(terrains);
    }
}
