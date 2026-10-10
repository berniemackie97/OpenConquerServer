using System.Security.Cryptography;
using System.Text;
using OpenConquer.Domain.World;

namespace OpenConquer.GameData.Tool.Maps;

internal static class MapBaseTerrainBinaryWriter
{
    public const ushort FormatVersion = 1;
    public const string FileExtension = ".ocbt";

    public static void Write(string destinationPath, MapBaseTerrain terrain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(terrain);

        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Terrain output requires a parent directory.", nameof(destinationPath));

        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.SequentialScan))
            {
                using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write("OCBT"u8);
                    writer.Write(FormatVersion);
                    writer.Write((ushort)0);
                    writer.Write(terrain.MapDataId);
                    writer.Write(checked((uint)terrain.Width));
                    writer.Write(checked((uint)terrain.Height));
                    writer.Write(checked((uint)terrain.Exits.Length));

                    foreach (MapBaseTerrainCell cell in terrain.Cells)
                    {
                        writer.Write(cell.SurfaceId);
                        writer.Write(cell.PassabilityFlag);
                        writer.Write(cell.Elevation);
                    }

                    foreach (MapTerrainExit exit in terrain.Exits)
                    {
                        writer.Write(exit.X);
                        writer.Write(exit.Y);
                        writer.Write(exit.PasswayIndex);
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
