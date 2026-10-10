using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public static class MapBaseTerrainFingerprint
{
    public static byte[] Compute(MapBaseTerrain terrain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        cancellationToken.ThrowIfCancellationRequested();

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> header = stackalloc byte[24];
        "OCBT"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], MapBaseTerrainBinaryReader.FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], terrain.MapDataId);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], checked((uint)terrain.Width));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], checked((uint)terrain.Height));
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], checked((uint)terrain.Exits.Length));
        hash.AppendData(header);

        Span<byte> cellBuffer = stackalloc byte[6];

        for (int index = 0; index < terrain.Cells.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            MapBaseTerrainCell cell = terrain.Cells[index];

            BinaryPrimitives.WriteUInt16LittleEndian(cellBuffer, cell.SurfaceId);
            BinaryPrimitives.WriteUInt16LittleEndian(cellBuffer[2..], cell.PassabilityFlag);
            BinaryPrimitives.WriteInt16LittleEndian(cellBuffer[4..], cell.Elevation);
            hash.AppendData(cellBuffer);
        }

        Span<byte> exitBuffer = stackalloc byte[12];

        for (int index = 0; index < terrain.Exits.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            MapTerrainExit exit = terrain.Exits[index];

            BinaryPrimitives.WriteInt32LittleEndian(exitBuffer, exit.X);
            BinaryPrimitives.WriteInt32LittleEndian(exitBuffer[4..], exit.Y);
            BinaryPrimitives.WriteUInt32LittleEndian(exitBuffer[8..], exit.PasswayIndex);
            hash.AppendData(exitBuffer);
        }

        return hash.GetHashAndReset();
    }
}
