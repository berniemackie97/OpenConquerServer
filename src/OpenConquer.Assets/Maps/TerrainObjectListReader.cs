using System.Buffers.Binary;
using System.Collections.Immutable;

namespace OpenConquer.Assets.Maps;

public readonly record struct TerrainObjectCell(ushort PassabilityFlag, ushort SurfaceId, short ElevationDelta);

public readonly record struct TerrainObjectEntry(int Width, int Height, int AnchorTileOffsetX, int AnchorTileOffsetY, ImmutableArray<TerrainObjectCell> Cells);

public sealed class TerrainObjectList
{
    internal TerrainObjectList(ImmutableArray<TerrainObjectEntry> entries)
    {
        Entries = entries;
    }

    public ImmutableArray<TerrainObjectEntry> Entries { get; }
}

public static class TerrainObjectListReader
{
    private const int ResourcePathsLength = 0x140;

    public static TerrainObjectList Read(Stream stream, int maximumEntries, long maximumCells,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            int count = ReadInt32(stream);

            if (count < 0 || count > maximumEntries)
            {
                throw new InvalidDataException($"Terrain-object entry count {count} exceeds the supported range.");
            }

            ImmutableArray<TerrainObjectEntry>.Builder entries = ImmutableArray.CreateBuilder<TerrainObjectEntry>(count);
            long totalCells = 0;

            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SkipExactly(stream, ResourcePathsLength);

                _ = ReadInt32(stream);
                _ = ReadInt32(stream);
                _ = ReadInt32(stream);

                int width = ReadInt32(stream);
                int height = ReadInt32(stream);

                if (width < 0 || height < 0)
                {
                    throw new InvalidDataException($"Terrain-object entry {index} has negative grid dimensions.");
                }

                long cellCount = (long)width * height;
                totalCells = checked(totalCells + cellCount);

                if (cellCount > int.MaxValue || totalCells > maximumCells)
                {
                    throw new InvalidDataException($"Terrain-object entry {index} exceeds the configured cell budget.");
                }

                _ = ReadInt32(stream);

                int anchorOffsetX = ReadInt32(stream);
                int anchorOffsetY = ReadInt32(stream);

                _ = ReadInt32(stream);

                ImmutableArray<TerrainObjectCell>.Builder cells =
                    ImmutableArray.CreateBuilder<TerrainObjectCell>(checked((int)cellCount));

                for (int row = 0; row < height; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (int column = 0; column < width; column++)
                    {
                        int passability = ReadInt32(stream);
                        int surface = ReadInt32(stream);
                        int elevation = ReadInt32(stream);

                        cells.Add(new TerrainObjectCell(
                            unchecked((ushort)passability),
                            unchecked((ushort)surface),
                            unchecked((short)elevation)));
                    }
                }

                entries.Add(new TerrainObjectEntry(width, height, anchorOffsetX, anchorOffsetY,
                    cells.MoveToImmutable()));
            }

            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Terrain-object list contains unexpected trailing data.");
            }

            return new TerrainObjectList(entries.MoveToImmutable());
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Terrain-object list is truncated.", exception);
        }
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static void SkipExactly(Stream stream, int length)
    {
        Span<byte> buffer = stackalloc byte[ResourcePathsLength];
        stream.ReadExactly(buffer[..length]);
    }
}
