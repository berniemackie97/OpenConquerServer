using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public sealed class FileMapTerrainCollisionCatalogRepository : IMapTerrainCollisionCatalogRepository
{
    private readonly string _rootPath;
    private readonly MapCollisionLoadLimits _limits;

    public FileMapTerrainCollisionCatalogRepository(string rootPath, MapCollisionLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(limits);

        _rootPath = Path.GetFullPath(rootPath);
        _limits = limits;
    }

    public async ValueTask<MapTerrainCollisionCatalog> LoadAsync(MapDefinitionCatalog definitions, MapBaseTerrainCatalog terrains, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(terrains);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo directory = new(_rootPath);

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Canonical collision directory '{_rootPath}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || directory.LinkTarget is not null)
        {
            throw new IOException($"Canonical collision directory '{_rootPath}' cannot be a symbolic link.");
        }

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        HashSet<string> expectedFiles = requiredIds.Select(mapDataId => $"{mapDataId}{MapCollisionBinaryFormat.FileExtension}").ToHashSet(StringComparer.Ordinal);

        foreach (string entry in Directory.EnumerateFileSystemEntries(_rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(entry);

            if (!expectedFiles.Remove(fileName))
            {
                throw new InvalidDataException($"Canonical collision directory contains unexpected entry '{fileName}'.");
            }
        }

        if (expectedFiles.Count != 0)
        {
            string missing = expectedFiles.Order(StringComparer.Ordinal).First();

            throw new FileNotFoundException($"Canonical collision directory is missing required artifact '{missing}'.", Path.Combine(_rootPath, missing));
        }

        List<MapTerrainCollision> collisions = new(requiredIds.Length);
        long totalOverrides = 0;

        foreach (uint mapDataId in requiredIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!terrains.TryGet(mapDataId, out MapBaseTerrain? terrain))
            {
                throw new InvalidDataException($"Required canonical base terrain {mapDataId} is missing.");
            }

            string path = Path.Combine(_rootPath, $"{mapDataId}{MapCollisionBinaryFormat.FileExtension}");

            MapTerrainCollision collision = await MapCollisionBinaryReader.ReadAsync(path, terrain, _limits, cancellationToken).ConfigureAwait(false);

            totalOverrides = checked(totalOverrides + collision.OverrideCount);

            if (totalOverrides > _limits.MaximumTotalOverrides)
            {
                throw new InvalidDataException($"Canonical collision catalog contains {totalOverrides} overrides, exceeding the configured maximum of {_limits.MaximumTotalOverrides}.");
            }

            collisions.Add(collision);
        }

        return new MapTerrainCollisionCatalog(collisions);
    }
}
