using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

internal static class MapBaseTerrainBinaryReader
{
    public const ushort FormatVersion = 1;
    public const string FileExtension = ".ocbt";

    private const int HeaderLength = 24;
    private const int CellLength = 6;
    private const int ExitLength = 12;
    private const int HashLength = 32;

    public static async ValueTask<MapBaseTerrain> ReadAsync(string path, uint expectedMapDataId,
        MapTerrainLoadLimits limits, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo file = new(path);
        FileAttributes attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0
            || file.LinkTarget is not null)
        {
            throw new IOException($"Terrain artifact '{path}' is not a regular file.");
        }

        long length = file.Length;

        if (length < HeaderLength + HashLength || length > limits.MaximumArtifactBytes)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' has unsupported length {length}.");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>(checked((int)length));

        await using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            try
            {
                await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"Terrain artifact '{path}' changed while being read.", exception);
            }

            byte[] trailing = new byte[1];

            if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException($"Terrain artifact '{path}' changed while being read.");
            }
        }

        ReadOnlySpan<byte> header = payload.AsSpan(0, HeaderLength);

        if (!header[..4].SequenceEqual("OCBT"u8))
        {
            throw new InvalidDataException($"Terrain artifact '{path}' has invalid magic.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);

        if (version != FormatVersion || reserved != 0)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' has an unsupported format header.");
        }

        uint mapDataId = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        uint rawWidth = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        uint rawHeight = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        uint rawExitCount = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);

        if (mapDataId != expectedMapDataId)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' contains MapDataId {mapDataId}, expected {expectedMapDataId}.");
        }

        if (rawWidth is 0 or > int.MaxValue || rawHeight is 0 or > int.MaxValue
            || rawWidth > limits.MaximumWidth || rawHeight > limits.MaximumHeight)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' has invalid dimensions {rawWidth}x{rawHeight}.");
        }

        long cellCount = (long)rawWidth * rawHeight;

        if (cellCount > limits.MaximumCellsPerTerrain || rawExitCount > limits.MaximumExitsPerTerrain)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' exceeds its cell or exit capacity.");
        }

        long expectedLength = HeaderLength + (cellCount * CellLength) + (rawExitCount * ExitLength) + HashLength;

        if (expectedLength != payload.Length)
        {
            throw new InvalidDataException($"Terrain artifact '{path}' has length {payload.Length}, expected {expectedLength}.");
        }

        ReadOnlySpan<byte> encoded = payload.AsSpan(0, payload.Length - HashLength);
        ReadOnlySpan<byte> expectedHash = payload.AsSpan(payload.Length - HashLength);
        byte[] actualHash = SHA256.HashData(encoded);

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new InvalidDataException($"Terrain artifact '{path}' failed its SHA-256 integrity check.");
        }

        int width = checked((int)rawWidth);
        int height = checked((int)rawHeight);
        MapBaseTerrainCell[] cells = new MapBaseTerrainCell[checked((int)cellCount)];
        MapTerrainExit[] exits = new MapTerrainExit[checked((int)rawExitCount)];
        int offset = HeaderLength;

        for (int index = 0; index < cells.Length; index++)
        {
            ushort surfaceId = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset));
            ushort passabilityFlag = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 2));
            short elevation = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 4));

            cells[index] = new MapBaseTerrainCell(surfaceId, passabilityFlag, elevation);
            offset += CellLength;
        }

        for (int index = 0; index < exits.Length; index++)
        {
            int x = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int y = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            uint passwayIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 8));

            exits[index] = new MapTerrainExit(x, y, passwayIndex);
            offset += ExitLength;
        }

        return new MapBaseTerrain(mapDataId, width, height, cells, exits);
    }
}
