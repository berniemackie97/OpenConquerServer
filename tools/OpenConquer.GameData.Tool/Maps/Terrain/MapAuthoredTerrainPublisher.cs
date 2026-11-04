using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.GameData.Tool.IO;
using OpenConquer.GameData.Tool.Maps.Collision;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Maps.Terrain;

internal static class MapAuthoredTerrainPublisher
{
    public static async Task<uint> PublishAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MapTerrainLoadLimits terrainLimits = MapTerrainLoadLimits.CreateDefault();
        MapCollisionLoadLimits collisionLimits = MapCollisionLoadLimits.CreateDefault();
        byte[] source = ToolSourceFileReader.Read(sourcePath, checked((int)terrainLimits.MaximumContainerBytes));
        MapBaseTerrain terrain = MapBaseTerrainSourceReader.Parse(source, terrainLimits);

        string destination = Path.GetFullPath(destinationPath);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("The terrain build destination already exists.");
        }

        string parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("The terrain build destination requires a parent directory.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(stage);
        try
        {
            string terrainsPath = Path.Combine(stage, "terrains");
            string collisionsPath = Path.Combine(stage, "collisions");
            Directory.CreateDirectory(terrainsPath);
            Directory.CreateDirectory(collisionsPath);

            string name = terrain.MapDataId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            cancellationToken.ThrowIfCancellationRequested();
            MapBaseTerrainBinaryWriter.Write(Path.Combine(terrainsPath, name + MapBaseTerrainBinaryWriter.FileExtension), terrain);
            MapCollisionBinaryWriter.Write(Path.Combine(collisionsPath, name + MapCollisionBinaryFormat.FileExtension),
                new MapTerrainCollision(terrain, []), collisionLimits, cancellationToken);

            await ValidateAsync(terrain, terrainsPath, collisionsPath, terrainLimits, collisionLimits, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }
        }

        return terrain.MapDataId;
    }

    private static async Task ValidateAsync(MapBaseTerrain expected, string terrainsPath, string collisionsPath,
        MapTerrainLoadLimits terrainLimits, MapCollisionLoadLimits collisionLimits, CancellationToken cancellationToken)
    {
        MapDefinitionCatalog definitions = new([new MapDefinition(expected.MapDataId, expected.MapDataId, 0)]);
        MapBaseTerrainCatalog terrains = await new FileMapBaseTerrainCatalogRepository(terrainsPath, terrainLimits)
            .LoadAsync(definitions, cancellationToken).ConfigureAwait(false);
        if (!terrains.TryGet(expected.MapDataId, out MapBaseTerrain? actual) ||
            actual.Width != expected.Width || actual.Height != expected.Height ||
            !actual.Cells.SequenceEqual(expected.Cells) || !actual.Exits.SequenceEqual(expected.Exits))
        {
            throw new InvalidDataException("Published terrain differs from its authored source.");
        }

        MapTerrainCollisionCatalog collisions = await new FileMapTerrainCollisionCatalogRepository(collisionsPath, collisionLimits)
            .LoadAsync(definitions, terrains, cancellationToken).ConfigureAwait(false);
        if (!collisions.TryGet(expected.MapDataId, out MapTerrainCollision? collision) ||
            collision.OverrideCount != 0)
        {
            throw new InvalidDataException("Published collision does not match the authored terrain.");
        }
    }
}
