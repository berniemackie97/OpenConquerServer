using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Maps.Collision;

internal static class MapCollisionContentGenerator
{
    public static async Task<MapCollisionSourceReport> GenerateAsync(string assetRootPath,
        string definitionsPath, string terrainDirectory, string destinationDirectory,
        MapTerrainLoadLimits terrainLimits, MapCollisionSourceLimits sourceLimits,
        MapCollisionLoadLimits collisionLimits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(terrainDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(terrainLimits);
        ArgumentNullException.ThrowIfNull(sourceLimits);
        ArgumentNullException.ThrowIfNull(collisionLimits);
        cancellationToken.ThrowIfCancellationRequested();

        string destination = Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Collision destination '{destination}' already exists. Generation will not replace existing content.");
        }

        MapCollisionSourceBuild build = await MapCollisionSourceVerifier.BuildAsync(assetRootPath, definitionsPath, terrainDirectory, terrainLimits, sourceLimits, cancellationToken).ConfigureAwait(false);

        await PublishAsync(build.Collisions, destination, collisionLimits, cancellationToken).ConfigureAwait(false);

        return build.Report;
    }

    internal static async Task PublishAsync(IEnumerable<MapTerrainCollision> collisions, string destinationDirectory, MapCollisionLoadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collisions);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        MapTerrainCollision[] ordered = collisions.OrderBy(collision => collision.MapDataId).ToArray();

        if (ordered.Length == 0)
        {
            throw new InvalidDataException("A canonical collision release cannot be empty.");
        }

        long totalOverrides = 0;
        uint previousMapDataId = 0;

        foreach (MapTerrainCollision collision in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (collision.MapDataId <= previousMapDataId)
            {
                throw new InvalidDataException($"Collision release contains duplicate or invalid MapDataId {collision.MapDataId}.");
            }

            previousMapDataId = collision.MapDataId;
            totalOverrides = checked(totalOverrides + collision.OverrideCount);

            if (collision.OverrideCount > limits.MaximumOverridesPerTerrain || totalOverrides > limits.MaximumTotalOverrides)
            {
                throw new InvalidDataException("Collision release exceeds its configured override capacity.");
            }

            long artifactLength = MapCollisionBinaryFormat.MinimumLength + ((long)collision.OverrideCount * MapCollisionBinaryFormat.OverrideLength);

            if (artifactLength > limits.MaximumArtifactBytes)
            {
                throw new InvalidDataException($"Collision for MapDataId {collision.MapDataId} exceeds its file-size limit.");
            }
        }

        string destination = Path.GetFullPath(destinationDirectory);

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Collision destination '{destination}' already exists. Generation will not replace existing content.");
        }

        string parentDirectory = Path.GetDirectoryName(destination) ?? throw new IOException("Collision destination requires a parent directory.");

        Directory.CreateDirectory(parentDirectory);

        string stagingDirectory = Path.Combine(parentDirectory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(stagingDirectory);

        try
        {
            foreach (MapTerrainCollision collision in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string path = Path.Combine(stagingDirectory, $"{collision.MapDataId}{MapCollisionBinaryFormat.FileExtension}");

                MapCollisionBinaryWriter.Write(path, collision, limits, cancellationToken);

                MapTerrainCollision roundTrip = await MapCollisionBinaryReader.ReadAsync(
                    path, collision.BaseTerrain, limits, cancellationToken).ConfigureAwait(false);

                if (roundTrip.MapDataId != collision.MapDataId || !roundTrip.Overrides.SequenceEqual(collision.Overrides))
                {
                    throw new InvalidDataException($"Canonical collision round-trip failed for MapDataId {collision.MapDataId}.");
                }
            }

            Directory.Move(stagingDirectory, destination);
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
