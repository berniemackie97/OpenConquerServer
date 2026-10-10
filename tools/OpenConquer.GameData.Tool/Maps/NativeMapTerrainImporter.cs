using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.Formats.Maps;
using OpenConquer.GameData.Tool.IO;

namespace OpenConquer.GameData.Tool.Maps;

internal static class NativeMapTerrainImporter
{
    public static int Generate(string assetRootPath, string definitionsPath, string destinationDirectory,
        MapTerrainLoadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        string destination = Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Terrain destination '{destination}' already exists. Generation will not replace existing content.");
        }

        NativeMapSourceFiles sources = new(assetRootPath, limits);

        byte[] definitionPayload = ToolSourceFileReader.Read(definitionsPath, MapDefinitionSourceReader.MaximumSourceLengthBytes);

        MapDefinition[] definitions = MapDefinitionSourceReader.Parse(definitionPayload);
        uint[] requiredIds = definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        string indexPath = sources.ResolveFile("ini/GameMap.dat");
        byte[] indexPayload = ToolSourceFileReader.Read(indexPath, GameMapDatTable.MaximumPayloadBytes);
        GameMapDatTable index = GameMapDatTable.Parse(indexPayload);

        string parentDirectory = Path.GetDirectoryName(destination) ?? throw new IOException("Terrain destination requires a parent directory.");

        Directory.CreateDirectory(parentDirectory);

        string stagingDirectory = Path.Combine(parentDirectory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(stagingDirectory);

        try
        {
            long totalCells = 0;

            foreach (uint mapDataId in requiredIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!index.TryGet(mapDataId, out GameMapDatRecord? record) || record is null)
                {
                    throw new InvalidDataException($"Native GameMap.dat has no terrain reference for MapDataId {mapDataId}.");
                }

                MapBaseTerrain terrain = sources.ReadMap(record.RelativePath, stream =>
                {
                    DMapBaseGrid grid = DMapBaseGridReader.Read(stream, limits.MaximumWidth,
                        limits.MaximumHeight, limits.MaximumCellsPerTerrain,
                        limits.MaximumExitsPerTerrain, cancellationToken);

                    MapBaseTerrainCell[] cells = grid.Cells.Select(cell => new MapBaseTerrainCell(cell.SurfaceId, cell.PassabilityFlag, cell.Elevation)).ToArray();

                    MapTerrainExit[] exits = grid.Exits.Select(exit => new MapTerrainExit(exit.X, exit.Y, exit.PasswayIndex)).ToArray();

                    return new MapBaseTerrain(mapDataId, grid.Width, grid.Height, cells, exits);
                }, cancellationToken);

                totalCells = checked(totalCells + terrain.CellCount);

                if (totalCells > limits.MaximumTotalCells)
                {
                    throw new InvalidDataException($"Native terrain contains {totalCells} cells, exceeding the aggregate limit of {limits.MaximumTotalCells}.");
                }

                string outputPath = Path.Combine(stagingDirectory, $"{mapDataId}{MapBaseTerrainBinaryWriter.FileExtension}");

                MapBaseTerrainBinaryWriter.Write(outputPath, terrain);
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
}
