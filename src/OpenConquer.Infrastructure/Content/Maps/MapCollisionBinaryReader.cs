using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Application.World;
using OpenConquer.Domain.World;

namespace OpenConquer.Infrastructure.Content.Maps;

public static class MapCollisionBinaryReader
{
    public static async ValueTask<MapTerrainCollision> ReadAsync(string path, MapBaseTerrain baseTerrain,
        MapCollisionLoadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(baseTerrain);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo file = new(path);
        FileAttributes attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0
            || file.LinkTarget is not null)
        {
            throw new IOException($"Collision artifact '{path}' is not a regular file.");
        }

        long length = file.Length;

        if (length < MapCollisionBinaryFormat.MinimumLength || length > limits.MaximumArtifactBytes)
        {
            throw new InvalidDataException($"Collision artifact '{path}' has unsupported length {length}.");
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
                throw new InvalidDataException($"Collision artifact '{path}' changed while being read.", exception);
            }

            byte[] trailing = new byte[1];

            if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException($"Collision artifact '{path}' changed while being read.");
            }
        }

        ReadOnlySpan<byte> header = payload.AsSpan(0, MapCollisionBinaryFormat.HeaderLength);

        if (!header[..4].SequenceEqual("OCMC"u8))
        {
            throw new InvalidDataException($"Collision artifact '{path}' has invalid magic.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);

        if (version != MapCollisionBinaryFormat.FormatVersion || reserved != 0)
        {
            throw new InvalidDataException($"Collision artifact '{path}' has an unsupported format header.");
        }

        uint mapDataId = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        uint width = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        uint overrideCount = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);

        if (mapDataId != baseTerrain.MapDataId
            || width != (uint)baseTerrain.Width || height != (uint)baseTerrain.Height)
        {
            throw new InvalidDataException($"Collision artifact '{path}' does not match its canonical base-terrain identity or dimensions.");
        }

        if (overrideCount > limits.MaximumOverridesPerTerrain || overrideCount > baseTerrain.CellCount)
        {
            throw new InvalidDataException($"Collision artifact '{path}' exceeds its override capacity.");
        }

        long expectedLength = MapCollisionBinaryFormat.MinimumLength
            + ((long)overrideCount * MapCollisionBinaryFormat.OverrideLength);

        if (expectedLength != payload.Length)
        {
            throw new InvalidDataException($"Collision artifact '{path}' has length {payload.Length}, expected {expectedLength}.");
        }

        ReadOnlySpan<byte> encoded = payload.AsSpan(0, payload.Length - MapCollisionBinaryFormat.HashLength);
        ReadOnlySpan<byte> expectedHash = payload.AsSpan(payload.Length - MapCollisionBinaryFormat.HashLength);

        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(encoded), expectedHash))
        {
            throw new InvalidDataException($"Collision artifact '{path}' failed its SHA-256 integrity check.");
        }

        byte[] baseFingerprint = MapBaseTerrainFingerprint.Compute(baseTerrain, cancellationToken);

        if (!CryptographicOperations.FixedTimeEquals(baseFingerprint, header.Slice(24, MapCollisionBinaryFormat.HashLength)))
        {
            throw new InvalidDataException($"Collision artifact '{path}' is bound to different canonical base-terrain content.");
        }

        MapTerrainCollisionOverride[] overrides = new MapTerrainCollisionOverride[checked((int)overrideCount)];
        int offset = MapCollisionBinaryFormat.HeaderLength;
        uint previousIndex = 0;

        for (int index = 0; index < overrides.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            uint cellIndex = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
            ushort surfaceId = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 4));
            ushort passabilityFlag = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 6));
            short elevation = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 8));

            if (cellIndex >= baseTerrain.CellCount || (index > 0 && cellIndex <= previousIndex))
            {
                throw new InvalidDataException($"Collision artifact '{path}' has an invalid or unordered override at record {index}.");
            }

            MapBaseTerrainCell cell = new(surfaceId, passabilityFlag, elevation);

            if (cell == baseTerrain.Cells[checked((int)cellIndex)])
            {
                throw new InvalidDataException($"Collision artifact '{path}' contains a redundant override at record {index}.");
            }

            overrides[index] = new MapTerrainCollisionOverride(checked((int)cellIndex), cell);
            previousIndex = cellIndex;
            offset += MapCollisionBinaryFormat.OverrideLength;
        }

        return new MapTerrainCollision(baseTerrain, overrides);
    }
}
