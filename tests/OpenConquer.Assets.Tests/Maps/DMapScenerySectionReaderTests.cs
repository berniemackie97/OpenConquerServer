using System.Text;
using OpenConquer.Assets.Maps;

namespace OpenConquer.Assets.Tests.Maps;

public sealed class DMapScenerySectionReaderTests
{
    [Fact]
    public void Read_KnownTypes_PreservesTerrainGroupsAndConsumesExactPayloads()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(4);

            writer.Write(0x01);
            WriteFixedString(writer, "map/scene/example.obj", 0x104);
            writer.Write(125);
            writer.Write(-30);

            writer.Write(0x04);
            writer.Write(new byte[0x1A0]);

            writer.Write(0x0A);
            writer.Write(new byte[0x48]);

            writer.Write(0x0F);
            writer.Write(new byte[0x114]);
        }

        stream.Position = 0;

        DMapScenerySection result = DMapScenerySectionReader.Read(
            stream, 10, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.SubcomponentCount);
        Assert.Single(result.TerrainObjectGroups);
        Assert.Equal(new DMapTerrainObjectGroup("map/scene/example.obj", 125, -30),
            result.TerrainObjectGroups[0]);

        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Read_AfterBaseGrid_BeginsAtScenerySection()
    {
        byte[] baseMap = DMapBaseGridReaderTests.CreateValidDMap();

        using MemoryStream stream = new();
        stream.Write(baseMap);

        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(0x01);
            WriteFixedString(writer, "map/scene/house.obj", 0x104);
            writer.Write(75);
            writer.Write(88);
        }

        stream.Position = 0;

        DMapBaseGrid grid = DMapBaseGridReader.Read(
            stream, 10, 10, 100, 10, TestContext.Current.CancellationToken);

        DMapScenerySection scenery = DMapScenerySectionReader.Read(
            stream, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, grid.Width);
        Assert.Equal(1, grid.Height);
        Assert.Single(scenery.TerrainObjectGroups);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Read_UnknownType_IsRejected()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(0x02);
        }

        stream.Position = 0;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => DMapScenerySectionReader.Read(stream, 10, TestContext.Current.CancellationToken));

        Assert.Contains("unsupported type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TruncatedPayload_IsRejected()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(0x04);
            writer.Write(new byte[0x10]);
        }

        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => DMapScenerySectionReader.Read(stream, 10, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    public void Read_InvalidCount_IsRejected(int count, int limit)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(count);
        }

        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => DMapScenerySectionReader.Read(stream, limit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_EmptyTerrainObjectPath_IsRejected()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(0x01);
            writer.Write(new byte[0x104]);
            writer.Write(0);
            writer.Write(0);
        }

        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => DMapScenerySectionReader.Read(stream, 10, TestContext.Current.CancellationToken));
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int length)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);

        Assert.True(bytes.Length < length);

        writer.Write(bytes);
        writer.Write(new byte[length - bytes.Length]);
    }
}
