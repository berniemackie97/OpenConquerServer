using System.Text;
using OpenConquer.Assets.Maps;

namespace OpenConquer.Assets.Tests.Maps;

public sealed class TerrainObjectListReaderTests
{
    [Fact]
    public void Read_ValidObject_PreservesDimensionsOffsetsAndLowWords()
    {
        using MemoryStream stream = CreateObjectList(2, 1);

        TerrainObjectList result = TerrainObjectListReader.Read(
            stream, 10, 100, TestContext.Current.CancellationToken);

        TerrainObjectEntry entry = Assert.Single(result.Entries);

        Assert.Equal(2, entry.Width);
        Assert.Equal(1, entry.Height);
        Assert.Equal(7, entry.AnchorTileOffsetX);
        Assert.Equal(-9, entry.AnchorTileOffsetY);

        Assert.Equal(new TerrainObjectCell(1, 0x8002, -1), entry.Cells[0]);
        Assert.Equal(new TerrainObjectCell(0, 24, 12), entry.Cells[1]);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Read_CellBudgetExceeded_IsRejected()
    {
        using MemoryStream stream = CreateObjectList(2, 1);

        Assert.Throws<InvalidDataException>(() => TerrainObjectListReader.Read(stream, 10, 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_EntryBudgetExceeded_IsRejected()
    {
        using MemoryStream stream = CreateObjectList(2, 1);

        Assert.Throws<InvalidDataException>(() => TerrainObjectListReader.Read(stream, 0, 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_TruncatedMetadata_IsRejected()
    {
        using MemoryStream source = CreateObjectList(2, 1);
        byte[] payload = source.ToArray();

        using MemoryStream stream = new(payload[..^4]);

        Assert.Throws<InvalidDataException>(() => TerrainObjectListReader.Read(stream, 10, 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_TrailingBytes_AreRejected()
    {
        using MemoryStream source = CreateObjectList(2, 1);
        byte[] payload = [.. source.ToArray(), 0xAA];

        using MemoryStream stream = new(payload);

        Assert.Throws<InvalidDataException>(() => TerrainObjectListReader.Read(stream, 10, 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_NegativeDimensions_AreRejected()
    {
        using MemoryStream stream = CreateObjectList(-1, 1);

        Assert.Throws<InvalidDataException>(() => TerrainObjectListReader.Read(stream, 10, 100, TestContext.Current.CancellationToken));
    }

    private static MemoryStream CreateObjectList(int width, int height)
    {
        MemoryStream stream = new();

        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(new byte[0x140]);

            writer.Write(0);
            writer.Write(0);
            writer.Write(30);

            writer.Write(width);
            writer.Write(height);

            writer.Write(0);
            writer.Write(7);
            writer.Write(-9);
            writer.Write(0);

            if (width > 0 && height > 0)
            {
                writer.Write(unchecked((int)0x12340001));
                writer.Write(unchecked((int)0xFEDC8002));
                writer.Write(unchecked((int)0xABCDFFFF));

                if (width * height > 1)
                {
                    writer.Write(0);
                    writer.Write(24);
                    writer.Write(12);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }
}
