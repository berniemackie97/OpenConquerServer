using System.Security.Cryptography;
using System.Text;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;
using OpenConquer.Infrastructure.Content.Maps;

namespace OpenConquer.GameData.Tool.Maps.Collision;

internal static class MapCollisionBinaryWriter
{
    public static void Write(string destinationPath, MapTerrainCollision collision,
        MapCollisionLoadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (collision.OverrideCount > limits.MaximumOverridesPerTerrain)
        {
            throw new InvalidDataException($"Collision for MapDataId {collision.MapDataId} exceeds the configured override limit.");
        }

        long length = MapCollisionBinaryFormat.MinimumLength + ((long)collision.OverrideCount * MapCollisionBinaryFormat.OverrideLength);

        if (length > limits.MaximumArtifactBytes)
        {
            throw new InvalidDataException($"Collision for MapDataId {collision.MapDataId} exceeds the configured artifact-size limit.");
        }

        byte[] baseFingerprint = MapBaseTerrainFingerprint.Compute(collision.BaseTerrain, cancellationToken);

        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("Collision output requires a parent directory.", nameof(destinationPath));

        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.SequentialScan))
            {
                using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write("OCMC"u8);
                    writer.Write(MapCollisionBinaryFormat.FormatVersion);
                    writer.Write((ushort)0);
                    writer.Write(collision.MapDataId);
                    writer.Write(checked((uint)collision.Width));
                    writer.Write(checked((uint)collision.Height));
                    writer.Write(checked((uint)collision.OverrideCount));
                    writer.Write(baseFingerprint);

                    foreach (MapTerrainCollisionOverride entry in collision.Overrides)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        writer.Write(checked((uint)entry.CellIndex));
                        writer.Write(entry.Cell.SurfaceId);
                        writer.Write(entry.Cell.PassabilityFlag);
                        writer.Write(entry.Cell.Elevation);
                    }

                    writer.Flush();
                }

                stream.Flush();
                stream.Position = 0;

                byte[] hash = SHA256.HashData(stream);

                stream.Position = stream.Length;
                stream.Write(hash);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
