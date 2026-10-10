using System.Text;
using OpenConquer.GameData.Tool.Formats.Maps;

namespace OpenConquer.GameData.Tool.Tests.Formats.Maps;

public sealed class GameMapDatTableTests
{
    [Fact]
    public void Parse_ValidRecords_PreservesLastRecordForDuplicateMapDataId()
    {
        byte[] payload = BuildIndex(
            (1000, "map/map/first.7z", 1),
            (1001, "map/map/other.7z", 2),
            (1000, "map/map/replacement.7z", 3));

        GameMapDatTable table = GameMapDatTable.Parse(payload);

        Assert.Equal(2, table.Count);
        Assert.True(table.TryGet(1000, out GameMapDatRecord? record));
        if (record != null)
        {
            Assert.Equal("map/map/replacement.7z", record.RelativePath);
            Assert.Equal(3u, record.ComponentLinkId);
        }
    }

    [Theory]
    [InlineData("../outside.7z")]
    [InlineData("map/../outside.7z")]
    [InlineData("/absolute.7z")]
    [InlineData("C:/map/test.7z")]
    [InlineData("map//test.7z")]
    [InlineData("map/test.zip")]
    [InlineData("map/./test.7z")]
    public void Parse_InvalidPath_IsRejected(string path)
    {
        byte[] payload = BuildIndex((1000, path, 0));

        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(payload));
    }

    [Fact]
    public void Parse_ZeroMapDataId_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(BuildIndex((0, "map/test.7z", 0))));
    }

    [Fact]
    public void Parse_PathExceedingNativeSlot_IsRejected()
    {
        string path = new string('a', GameMapDatTable.MaximumPathBytes) + ".7z";

        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(BuildIndex((1, path, 0))));
    }

    [Fact]
    public void Parse_TruncatedRecord_IsRejected()
    {
        byte[] payload = BuildIndex((1, "map/test.7z", 0));

        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(payload[..^1]));
    }

    [Fact]
    public void Parse_TrailingBytes_AreRejected()
    {
        byte[] payload = BuildIndex((1, "map/test.7z", 0));
        byte[] extended = [.. payload, 0xA5];

        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(extended));
    }

    [Fact]
    public void Parse_DeclaredRecordCountAboveLimit_IsRejected()
    {
        byte[] payload = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload, GameMapDatTable.MaximumRecords + 1u);

        Assert.Throws<InvalidDataException>(() => GameMapDatTable.Parse(payload));
    }

    private static byte[] BuildIndex(params (uint Id, string Path, uint ComponentLinkId)[] records)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(checked((uint)records.Length));

        foreach ((uint id, string path, uint componentLinkId) in records)
        {
            byte[] pathBytes = Encoding.ASCII.GetBytes(path);

            writer.Write(id);
            writer.Write(checked((uint)pathBytes.Length));
            writer.Write(pathBytes);
            writer.Write(componentLinkId);
        }

        writer.Flush();

        return stream.ToArray();
    }
}
