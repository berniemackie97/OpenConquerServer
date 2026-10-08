using OpenConquer.Application.World;
using OpenConquer.Assets.Maps;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenConquer.GameData.Tool.Maps;

internal static class NativeMapTerrainImporter
{
    private const string IndexRelativePath = "ini/GameMap.dat";

    public static int Generate(string assetRootPath, string definitionsPath, string destinationDirectory,
        MapTerrainLoadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(assetRootPath);
        string destination = Path.GetFullPath(destinationDirectory);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Native asset root '{root}' does not exist.");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Terrain destination '{destination}' already exists. Generation will not replace existing content.");
        }

        byte[] definitionPayload = ToolSourceFileReader.Read(definitionsPath, MapDefinitionSourceReader.MaximumSourceLengthBytes);
        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(definitionPayload);
        uint[] requiredIds = definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        string indexPath = ResolveContainedFile(root, IndexRelativePath);
        byte[] indexPayload = ToolSourceFileReader.Read(indexPath, GameMapDatTable.MaximumPayloadBytes);
        GameMapDatTable index = GameMapDatTable.Parse(indexPayload);

        string parentDirectory = Path.GetDirectoryName(destination)
            ?? throw new IOException("Terrain destination requires a parent directory.");

        Directory.CreateDirectory(parentDirectory);

        string stagingDirectory = Path.Combine(parentDirectory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            long totalCells = 0;

            foreach (uint mapDataId in requiredIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!index.TryGet(mapDataId, out GameMapDatRecord? record))
                {
                    throw new InvalidDataException($"Native GameMap.dat has no terrain reference for MapDataId {mapDataId}.");
                }

                if (record != null)
                {
                    string containerPath = ResolveContainedFile(root, record.RelativePath);
                    MapBaseTerrain terrain = ReadTerrain(containerPath, mapDataId, limits, cancellationToken);

                    totalCells = checked(totalCells + terrain.CellCount);

                    if (totalCells > limits.MaximumTotalCells)
                    {
                        throw new InvalidDataException($"Native terrain contains {totalCells} cells, exceeding the aggregate limit of {limits.MaximumTotalCells}.");
                    }

                    string outputPath = Path.Combine(stagingDirectory, $"{mapDataId}{MapBaseTerrainBinaryWriter.FileExtension}");
                    MapBaseTerrainBinaryWriter.Write(outputPath, terrain);
                }
            }

            Directory.Move(stagingDirectory, destination);

            return requiredIds.Length;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static MapBaseTerrain ReadTerrain(string containerPath, uint mapDataId, MapTerrainLoadLimits limits,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(containerPath);

        if (!file.Exists || file.Length is <= 0 || file.Length > limits.MaximumContainerBytes)
        {
            throw new InvalidDataException($"Native map container '{containerPath}' is missing, empty, or oversized.");
        }

        string extension = Path.GetExtension(containerPath);

        using FileStream stream = new(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);

        if (string.Equals(extension, ".DMap", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertGrid(stream, mapDataId, limits, cancellationToken);
        }

        if (!string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported native map container '{extension}'.");
        }

        try
        {
            if (!SevenZipArchive.IsSevenZipFile(stream))
            {
                throw new InvalidDataException($"Native map container '{containerPath}' has an invalid 7-Zip signature.");
            }

            stream.Position = 0;

            using IArchive archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
            IArchiveEntry? payloadEntry = null;

            foreach (IArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    continue;
                }

                if (payloadEntry is not null)
                {
                    throw new InvalidDataException($"Native map container '{containerPath}' contains multiple file payloads.");
                }

                payloadEntry = entry;
            }

            if (payloadEntry is null || payloadEntry.Size is <= 0 || payloadEntry.Size > limits.MaximumContainerBytes)
            {
                throw new InvalidDataException($"Native map container '{containerPath}' has no valid bounded payload.");
            }

            string entryName = payloadEntry.Key
                ?? throw new InvalidDataException($"Native map container '{containerPath}' contains an unnamed payload.");

            string normalizedName = entryName.Replace('\\', '/');

            if (normalizedName.StartsWith('/') || normalizedName.Contains(':')
                || normalizedName.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            {
                throw new InvalidDataException($"Native map container '{containerPath}' contains an invalid entry path.");
            }

            string payloadName = Path.GetFileName(normalizedName);
            string expectedName = Path.GetFileNameWithoutExtension(containerPath);
            string actualName = Path.GetFileNameWithoutExtension(payloadName);

            if (!string.Equals(Path.GetExtension(payloadName), ".DMap", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Native map container '{containerPath}' has an unexpected DMap payload name.");
            }

            using Stream payload = payloadEntry.OpenEntryStream();

            return ConvertGrid(payload, mapDataId, limits, cancellationToken);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArchiveException or IOException or NotSupportedException
            or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Native map container '{containerPath}' could not be decoded.", exception);
        }
    }

    private static MapBaseTerrain ConvertGrid(Stream stream, uint mapDataId, MapTerrainLoadLimits limits,
        CancellationToken cancellationToken)
    {
        DMapBaseGrid grid = DMapBaseGridReader.Read(stream, limits.MaximumWidth, limits.MaximumHeight,
            limits.MaximumCellsPerTerrain, limits.MaximumExitsPerTerrain, cancellationToken);

        MapBaseTerrainCell[] cells = grid.Cells.Select(cell =>
            new MapBaseTerrainCell(cell.SurfaceId, cell.PassabilityFlag, cell.Elevation)).ToArray();

        MapTerrainExit[] exits = grid.Exits.Select(exit =>
            new MapTerrainExit(exit.X, exit.Y, exit.PasswayIndex)).ToArray();

        return new MapBaseTerrain(mapDataId, grid.Width, grid.Height, cells, exits);
    }

    private static string ResolveContainedFile(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relativeToRoot = Path.GetRelativePath(rootPath, fullPath);

        if (relativeToRoot is "." or ".." || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativeToRoot))
        {
            throw new InvalidDataException($"Native asset path '{relativePath}' escapes its configured root.");
        }

        DirectoryInfo rootDirectory = new(rootPath);

        if ((rootDirectory.Attributes & FileAttributes.ReparsePoint) != 0 || rootDirectory.LinkTarget is not null)
        {
            throw new IOException($"Native asset root '{rootPath}' is a symbolic link.");
        }

        string current = rootPath;
        string[] segments = relativeToRoot.Split(Path.DirectorySeparatorChar);

        foreach (string segment in segments)
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes = File.GetAttributes(current);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Native asset path '{current}' traverses a symbolic link.");
            }
        }

        return fullPath;
    }
}
