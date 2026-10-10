using System.Buffers.Binary;

namespace OpenConquer.GameData.Tool.Formats.Maps;

public readonly record struct DMapBaseCell(ushort SurfaceId, ushort PassabilityFlag, short Elevation);
public readonly record struct DMapExitMarker(int X, int Y, uint PasswayIndex);

/// <summary>
/// Verified native first-grid and exit-marker sections. Later DMap sections are not consumed.
/// </summary>
public sealed class DMapBaseGrid
{
    private readonly DMapBaseCell[] _cells;
    private readonly DMapExitMarker[] _exits;

    internal DMapBaseGrid(int width, int height, DMapBaseCell[] cells, DMapExitMarker[] exits)
    {
        Width = width;
        Height = height;
        _cells = cells;
        _exits = exits;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<DMapBaseCell> Cells => Array.AsReadOnly(_cells);
    public IReadOnlyList<DMapExitMarker> Exits => Array.AsReadOnly(_exits);
}

public static class DMapBaseGridReader
{
    private const int PuzzlePathLength = 0x104;

    public static DMapBaseGrid Read(Stream stream, int maximumWidth, int maximumHeight,
        int maximumCells, int maximumExits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExits);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _ = ReadUInt32(stream);
            _ = ReadUInt32(stream);

            Span<byte> puzzlePath = stackalloc byte[PuzzlePathLength];
            stream.ReadExactly(puzzlePath);

            uint rawWidth = ReadUInt32(stream);
            uint rawHeight = ReadUInt32(stream);

            if (rawWidth is 0 or > int.MaxValue || rawHeight is 0 or > int.MaxValue
                || rawWidth > maximumWidth || rawHeight > maximumHeight)
            {
                throw new InvalidDataException($"DMap contains invalid grid dimensions {rawWidth}x{rawHeight}.");
            }

            long cellCount = (long)rawWidth * rawHeight;

            if (cellCount > maximumCells)
            {
                throw new InvalidDataException($"DMap contains {cellCount} cells, exceeding the configured maximum of {maximumCells}.");
            }

            int width = checked((int)rawWidth);
            int height = checked((int)rawHeight);
            DMapBaseCell[] cells = new DMapBaseCell[checked((int)cellCount)];

            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                uint checksum = 0;

                for (int x = 0; x < width; x++)
                {
                    ushort passabilityFlag = ReadUInt16(stream);
                    ushort surfaceId = ReadUInt16(stream);
                    short elevation = unchecked((short)ReadUInt16(stream));

                    cells[(y * width) + x] = new DMapBaseCell(surfaceId, passabilityFlag, elevation);

                    checksum = unchecked(checksum
                        + (uint)((surfaceId + y + 1) * passabilityFlag)
                        + (uint)((surfaceId + x + 1) * (elevation + 2)));
                }

                uint recordedChecksum = ReadUInt32(stream);

                if (checksum != recordedChecksum)
                {
                    throw new InvalidDataException($"DMap row {y} checksum mismatch: expected 0x{recordedChecksum:X8}, calculated 0x{checksum:X8}.");
                }
            }

            uint rawExitCount = ReadUInt32(stream);

            if (rawExitCount > maximumExits)
            {
                throw new InvalidDataException($"DMap contains {rawExitCount} exit markers, exceeding the configured maximum of {maximumExits}.");
            }

            DMapExitMarker[] exits = new DMapExitMarker[checked((int)rawExitCount)];

            for (int index = 0; index < exits.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int x = unchecked((int)ReadUInt32(stream));
                int y = unchecked((int)ReadUInt32(stream));
                uint passwayIndex = ReadUInt32(stream);

                exits[index] = new DMapExitMarker(x, y, passwayIndex);
            }

            return new DMapBaseGrid(width, height, cells, exits);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("DMap base-grid or exit-marker section is truncated.", exception);
        }
    }

    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }
}
