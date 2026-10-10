using OpenConquer.Application.World;
using OpenConquer.Assets.Maps;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.IO;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Maps;

internal sealed record MapCollisionSourceResult(uint MapDataId, int ScenerySubcomponents, int TerrainObjectGroups, int ObjectListFiles, long Attachments, int EffectiveOverrides);

internal sealed record MapCollisionSourceReport(IReadOnlyList<MapCollisionSourceResult> Terrains, long TotalAttachments, long TotalObjectBytes)
{
    public int TerrainCount => Terrains.Count;
    public long TotalGroups => Terrains.Sum(terrain => (long)terrain.TerrainObjectGroups);
    public long TotalEffectiveOverrides => Terrains.Sum(terrain => (long)terrain.EffectiveOverrides);
}

internal static class MapCollisionSourceVerifier
{
    public static async Task<MapCollisionSourceReport> VerifyAsync(string assetRootPath,
        string definitionsPath, string terrainDirectory, MapTerrainLoadLimits terrainLimits,
        MapCollisionSourceLimits collisionLimits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(terrainDirectory);
        ArgumentNullException.ThrowIfNull(terrainLimits);
        ArgumentNullException.ThrowIfNull(collisionLimits);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] definitionPayload = ToolSourceFileReader.Read(definitionsPath, MapDefinitionSourceReader.MaximumSourceLengthBytes);

        MapDefinitionCatalog definitions = new(MapDefinitionSourceReader.Parse(definitionPayload));

        FileMapBaseTerrainCatalogRepository repository = new(terrainDirectory, terrainLimits);

        MapBaseTerrainCatalog canonical = await repository.LoadAsync(definitions, cancellationToken).ConfigureAwait(false);

        NativeMapSourceFiles sources = new(assetRootPath, terrainLimits);

        string indexPath = sources.ResolveFile("ini/GameMap.dat");

        GameMapDatTable index = GameMapDatTable.Parse(ToolSourceFileReader.Read(indexPath, GameMapDatTable.MaximumPayloadBytes));

        uint[] requiredIds = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Order().ToArray();

        List<MapCollisionSourceResult> results = new(requiredIds.Length);
        long totalAttachments = 0;
        long totalObjectBytes = 0;

        foreach (uint mapDataId in requiredIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!canonical.TryGet(mapDataId, out MapBaseTerrain? baseTerrain))
                {
                    throw new InvalidDataException("Required canonical terrain is missing.");
                }

                if (!index.TryGet(mapDataId, out GameMapDatRecord? record) || record is null)
                {
                    throw new InvalidDataException("GameMap.dat has no entry for the required terrain identity.");
                }

                SourceSections sections = sources.ReadMap(record.RelativePath, stream =>
                {
                    DMapBaseGrid grid = DMapBaseGridReader.Read(stream, terrainLimits.MaximumWidth, terrainLimits.MaximumHeight, terrainLimits.MaximumCellsPerTerrain, terrainLimits.MaximumExitsPerTerrain, cancellationToken);

                    DMapScenerySection scenery = DMapScenerySectionReader.Read(stream, collisionLimits.MaximumScenerySubcomponents, cancellationToken);

                    return new SourceSections(grid, scenery);
                }, cancellationToken);

                VerifyBaseTerrain(baseTerrain, sections.Grid, cancellationToken);

                Dictionary<string, TerrainObjectList> objectLists = new(StringComparer.OrdinalIgnoreCase);

                TerrainObjectList LoadObjectList(string relativePath)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string resolvedPath = sources.ResolveFile(relativePath);

                    if (objectLists.TryGetValue(resolvedPath, out TerrainObjectList? cached))
                    {
                        return cached;
                    }

                    if (objectLists.Count >= collisionLimits.MaximumObjectListsPerTerrain)
                    {
                        throw new InvalidDataException($"Terrain references more than {collisionLimits.MaximumObjectListsPerTerrain} distinct object lists.");
                    }

                    byte[] payload = ToolSourceFileReader.Read(resolvedPath, collisionLimits.MaximumObjectFileBytes);

                    totalObjectBytes = checked(totalObjectBytes + payload.Length);

                    if (totalObjectBytes > collisionLimits.MaximumTotalObjectBytes)
                    {
                        throw new InvalidDataException($"Terrain-object source data exceeds the aggregate limit of {collisionLimits.MaximumTotalObjectBytes} bytes.");
                    }

                    using MemoryStream stream = new(payload, writable: false);

                    TerrainObjectList objects = TerrainObjectListReader.Read(stream, collisionLimits.MaximumObjectsPerList, collisionLimits.MaximumCellsPerList, cancellationToken);

                    objectLists.Add(resolvedPath, objects);
                    return objects;
                }

                IEnumerable<MapTerrainAttachment> sourceAttachments = DMapTerrainAttachmentSource.Enumerate(sections.Scenery, LoadObjectList);

                long attachmentCount = 0;

                IEnumerable<MapTerrainAttachment> CountAttachments()
                {
                    foreach (MapTerrainAttachment attachment in sourceAttachments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        attachmentCount++;
                        yield return attachment;
                    }
                }

                MapTerrainCollision collision = MapTerrainCollisionComposer.Compose(baseTerrain, CountAttachments(), collisionLimits.MaximumAttachmentsPerTerrain);

                totalAttachments = checked(totalAttachments + attachmentCount);

                if (totalAttachments > collisionLimits.MaximumTotalAttachments)
                {
                    throw new InvalidDataException($"Terrain attachments exceed the aggregate limit of {collisionLimits.MaximumTotalAttachments}.");
                }

                results.Add(new MapCollisionSourceResult(mapDataId, sections.Scenery.SubcomponentCount, sections.Scenery.TerrainObjectGroups.Length, objectLists.Count, attachmentCount, collision.OverrideCount));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException
                or UnauthorizedAccessException or OverflowException or ArgumentException)
            {
                throw new InvalidDataException($"MapDataId {mapDataId}: {exception.Message}", exception);
            }
        }

        return new MapCollisionSourceReport(results.ToArray(), totalAttachments, totalObjectBytes);
    }

    private static void VerifyBaseTerrain(MapBaseTerrain expected, DMapBaseGrid actual, CancellationToken cancellationToken)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new InvalidDataException(
                $"Base terrain dimensions disagree: canonical {expected.Width}x{expected.Height}, source {actual.Width}x{actual.Height}.");
        }

        if (expected.Cells.Length != actual.Cells.Count)
        {
            throw new InvalidDataException("Base terrain cell counts disagree.");
        }

        for (int index = 0; index < expected.Cells.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            DMapBaseCell source = actual.Cells[index];
            MapBaseTerrainCell cell = new(source.SurfaceId, source.PassabilityFlag, source.Elevation);

            if (expected.Cells[index] != cell)
            {
                int x = index % expected.Width;
                int y = index / expected.Width;

                throw new InvalidDataException($"Canonical base terrain differs at tile ({x}, {y}).");
            }
        }

        if (expected.Exits.Length != actual.Exits.Count)
        {
            throw new InvalidDataException("Base terrain exit-marker counts disagree.");
        }

        for (int index = 0; index < expected.Exits.Length; index++)
        {
            DMapExitMarker source = actual.Exits[index];
            MapTerrainExit marker = new(source.X, source.Y, source.PasswayIndex);

            if (expected.Exits[index] != marker)
            {
                throw new InvalidDataException($"Canonical base terrain differs at exit marker {index}.");
            }
        }
    }

    private sealed record SourceSections(DMapBaseGrid Grid, DMapScenerySection Scenery);
}
