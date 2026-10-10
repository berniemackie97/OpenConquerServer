using OpenConquer.GameData.Tool.Formats.Maps;

namespace OpenConquer.GameData.Tool.Tests.Formats.Maps;

public sealed class DMapBaseGridReaderTests
{
    [Fact]
    public void Read_ValidGrid_PreservesFieldOrderSignedElevationAndExits()
    {
        byte[] payload = CreateValidDMap();

        using MemoryStream stream = new(payload);

        DMapBaseGrid result = DMapBaseGridReader.Read(stream, 10, 10, 100, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Width);
        Assert.Equal(1, result.Height);

        Assert.Equal(new DMapBaseCell(12, 0, -15), result.Cells[0]);
        Assert.Equal(new DMapBaseCell(24, 1, 32), result.Cells[1]);

        Assert.Single(result.Exits);
        Assert.Equal(new DMapExitMarker(100, -20, 7), result.Exits[0]);
    }

    [Fact]
    public void Read_InvalidChecksum_IsRejected()
    {
        byte[] payload = CreateValidDMap();
        payload[8 + 260 + 8] ^= 1;

        using MemoryStream stream = new(payload);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            DMapBaseGridReader.Read(stream, 10, 10, 100, 10, TestContext.Current.CancellationToken));

        Assert.Contains("checksum mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TruncatedExit_IsRejected()
    {
        byte[] payload = CreateValidDMap();

        using MemoryStream stream = new(payload[..^1]);

        Assert.Throws<InvalidDataException>(() => DMapBaseGridReader.Read(stream, 10, 10, 100, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_GridExceedingCellBudget_IsRejected()
    {
        using MemoryStream stream = new(CreateValidDMap());

        Assert.Throws<InvalidDataException>(() => DMapBaseGridReader.Read(stream, 10, 10, 1, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_GridExceedingDimensionBudget_IsRejected()
    {
        using MemoryStream stream = new(CreateValidDMap());

        Assert.Throws<InvalidDataException>(() => DMapBaseGridReader.Read(stream, 1, 10, 100, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Read_LaterNativeSections_AreNotMistakenForGridData()
    {
        byte[] payload = [.. CreateValidDMap(), 0xAA, 0xBB, 0xCC, 0xDD];

        using MemoryStream stream = new(payload);
        DMapBaseGrid result = DMapBaseGridReader.Read(stream, 10, 10, 100, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Cells.Count);
        Assert.Equal(payload.Length - 4, stream.Position);
    }

    internal static byte[] CreateValidDMap()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write(0u);
        writer.Write(0u);
        writer.Write(new byte[0x104]);
        writer.Write(2u);
        writer.Write(1u);

        writer.Write((ushort)0);
        writer.Write((ushort)12);
        writer.Write((short)-15);

        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write((short)32);

        uint checksum = unchecked(
            (uint)((12 + 0 + 1) * 0)
            + (uint)((12 + 0 + 1) * (-15 + 2))
            + (uint)((24 + 0 + 1) * 1)
            + (uint)((24 + 1 + 1) * (32 + 2)));

        writer.Write(checksum);

        writer.Write(1u);
        writer.Write(100);
        writer.Write(-20);
        writer.Write(7u);
        writer.Flush();

        return stream.ToArray();
    }
}
