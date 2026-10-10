using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace OpenConquer.Assets.Maps;

public readonly record struct DMapTerrainObjectGroup(string ListPath, int AnchorTileX, int AnchorTileY);

public sealed class DMapScenerySection
{
    internal DMapScenerySection(int subcomponentCount, ImmutableArray<DMapTerrainObjectGroup> terrainObjectGroups)
    {
        SubcomponentCount = subcomponentCount;
        TerrainObjectGroups = terrainObjectGroups;
    }

    public int SubcomponentCount { get; }
    public ImmutableArray<DMapTerrainObjectGroup> TerrainObjectGroups { get; }
}

public static class DMapScenerySectionReader
{
    private const int TerrainObjectGroupType = 0x01;
    private const int AnimatedSpriteType = 0x04;
    private const int EffectType = 0x0A;
    private const int SoundType = 0x0F;
    private const int TerrainObjectPathLength = 0x104;

    public static DMapScenerySection Read(Stream stream, int maximumSubcomponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSubcomponents);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            int count = ReadInt32(stream);

            if (count < 0 || count > maximumSubcomponents)
            {
                throw new InvalidDataException($"DMap scenery subcomponent count {count} exceeds the supported range.");
            }

            ImmutableArray<DMapTerrainObjectGroup>.Builder groups = ImmutableArray.CreateBuilder<DMapTerrainObjectGroup>();

            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int type = ReadInt32(stream);

                switch (type)
                {
                    case TerrainObjectGroupType:
                        {
                            string path = ReadFixedString(stream, TerrainObjectPathLength);

                            if (string.IsNullOrWhiteSpace(path))
                            {
                                throw new InvalidDataException($"DMap scenery entry {index} has an empty terrain-object list path.");
                            }

                            int anchorX = ReadInt32(stream);
                            int anchorY = ReadInt32(stream);
                            groups.Add(new DMapTerrainObjectGroup(path, anchorX, anchorY));
                            break;
                        }
                    case AnimatedSpriteType:
                        SkipExactly(stream, 0x1A0);
                        break;
                    case EffectType:
                        SkipExactly(stream, 0x48);
                        break;
                    case SoundType:
                        SkipExactly(stream, 0x114);
                        break;
                    default:
                        throw new InvalidDataException($"DMap scenery entry {index} has unsupported type 0x{type:X8}.");
                }
            }

            return new DMapScenerySection(count, groups.ToImmutable());
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("DMap scenery section is truncated.", exception);
        }
    }

    private static string ReadFixedString(Stream stream, int length)
    {
        Span<byte> buffer = stackalloc byte[length];
        stream.ReadExactly(buffer);

        int terminator = buffer.IndexOf((byte)0);
        ReadOnlySpan<byte> value = terminator >= 0 ? buffer[..terminator] : buffer;

        return Encoding.Latin1.GetString(value);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static void SkipExactly(Stream stream, int length)
    {
        Span<byte> buffer = stackalloc byte[0x1A0];
        stream.ReadExactly(buffer[..length]);
    }
}
