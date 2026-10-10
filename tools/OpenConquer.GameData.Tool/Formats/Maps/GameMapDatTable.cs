using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text;

namespace OpenConquer.GameData.Tool.Formats.Maps;

internal sealed record GameMapDatRecord(uint MapDataId, string RelativePath, uint ComponentLinkId);

/// <summary>
/// Native 5517 GameMap.dat index. Duplicate MapDataIds use the last record.
/// </summary>
internal sealed class GameMapDatTable
{
    public const int MaximumPathBytes = 0x40;
    public const int MaximumRecords = 10_000;
    public const int MaximumPayloadBytes = 1024 * 1024;

    private readonly FrozenDictionary<uint, GameMapDatRecord> _records;

    private GameMapDatTable(Dictionary<uint, GameMapDatRecord> records)
    {
        _records = records.ToFrozenDictionary();
    }

    public int Count => _records.Count;

    public bool TryGet(uint mapDataId, out GameMapDatRecord? record)
    {
        return _records.TryGetValue(mapDataId, out record);
    }

    public static GameMapDatTable Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length < 4 || payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("GameMap.dat has an invalid file length.");
        }

        ReadOnlySpan<byte> data = payload;
        int offset = 0;
        uint declaredCount = ReadUInt32(data, ref offset);

        if (declaredCount is 0 or > MaximumRecords)
        {
            throw new InvalidDataException($"GameMap.dat declares invalid record count {declaredCount}.");
        }

        Dictionary<uint, GameMapDatRecord> records = new(checked((int)declaredCount));

        for (int index = 0; index < declaredCount; index++)
        {
            uint mapDataId = ReadUInt32(data, ref offset);
            uint rawPathLength = ReadUInt32(data, ref offset);

            if (mapDataId == 0 || rawPathLength is 0 or > MaximumPathBytes)
            {
                throw new InvalidDataException($"GameMap.dat record {index} contains an invalid identity or path length.");
            }

            int pathLength = checked((int)rawPathLength);

            if (data.Length - offset < pathLength)
            {
                throw new InvalidDataException($"GameMap.dat record {index} has a truncated path.");
            }

            ReadOnlySpan<byte> pathBytes = data.Slice(offset, pathLength);
            offset += pathLength;

            string relativePath = DecodeRelativePath(pathBytes, index);
            uint componentLinkId = ReadUInt32(data, ref offset);

            records[mapDataId] = new GameMapDatRecord(mapDataId, relativePath, componentLinkId);
        }

        if (offset != data.Length)
        {
            throw new InvalidDataException("GameMap.dat contains trailing bytes.");
        }

        return new GameMapDatTable(records);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < 4)
        {
            throw new InvalidDataException("GameMap.dat contains a truncated integer field.");
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static string DecodeRelativePath(ReadOnlySpan<byte> bytes, int recordIndex)
    {
        foreach (byte value in bytes)
        {
            if (value is < 0x20 or > 0x7E || value is (byte)':' or (byte)'\\')
            {
                if (value != (byte)'\\')
                {
                    throw new InvalidDataException($"GameMap.dat record {recordIndex} contains an invalid path character.");
                }
            }
        }

        string path = Encoding.ASCII.GetString(bytes).Replace('\\', '/');

        if (path.StartsWith('/') || path.Contains(':'))
        {
            throw new InvalidDataException($"GameMap.dat record {recordIndex} contains a nonrelative path.");
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new InvalidDataException($"GameMap.dat record {recordIndex} contains an invalid path segment.");
            }
        }

        string extension = Path.GetExtension(path);

        if (!string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".DMap", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GameMap.dat record {recordIndex} references an unsupported container.");
        }

        return path;
    }
}
