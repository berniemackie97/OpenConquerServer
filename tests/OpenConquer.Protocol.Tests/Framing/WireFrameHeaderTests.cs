using OpenConquer.Protocol.Framing;

namespace OpenConquer.Protocol.Tests.Framing;

public sealed class WireFrameHeaderTests
{
    [Fact]
    public void Size_IsFourBytes()
    {
        Assert.Equal(4, WireFrameHeader.Size);
    }

    [Fact]
    public void Constructor_ExposesLengthAndPacketId()
    {
        WireFrameHeader header = new(length: 0x1234, packetId: 0x5678);

        Assert.Equal(0x1234, header.Length);
        Assert.Equal(0x5678, header.PacketId);
    }

    [Fact]
    public void TryRead_ReadsLittleEndianHeader()
    {
        ReadOnlySpan<byte> source = [0x34, 0x12, 0x78, 0x56];

        bool success = WireFrameHeader.TryRead(source, out WireFrameHeader header);

        Assert.True(success);
        Assert.Equal(0x1234, header.Length);
        Assert.Equal(0x5678, header.PacketId);
    }

    [Fact]
    public void TryRead_IgnoresBytesBeyondHeader()
    {
        ReadOnlySpan<byte> source = [0x04, 0x00, 0x34, 0x12, 0xAA, 0xBB];

        bool success = WireFrameHeader.TryRead(source, out WireFrameHeader header);

        Assert.True(success);
        Assert.Equal(4, header.Length);
        Assert.Equal(0x1234, header.PacketId);
    }

    [Fact]
    public void TryRead_AcceptsZeroLengthAndPacketIdAsRawHeaderValues()
    {
        ReadOnlySpan<byte> source = [0x00, 0x00, 0x00, 0x00];

        bool success = WireFrameHeader.TryRead(source, out WireFrameHeader header);

        Assert.True(success);
        Assert.Equal(0, header.Length);
        Assert.Equal(0, header.PacketId);
    }

    [Fact]
    public void TryRead_AcceptsMaximumUInt16Values()
    {
        ReadOnlySpan<byte> source = [0xFF, 0xFF, 0xFF, 0xFF];

        bool success = WireFrameHeader.TryRead(source, out WireFrameHeader header);

        Assert.True(success);
        Assert.Equal(ushort.MaxValue, header.Length);
        Assert.Equal(ushort.MaxValue, header.PacketId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TryRead_ReturnsFalseForUndersizedSource(int sourceLength)
    {
        byte[] source = new byte[sourceLength];

        bool success = WireFrameHeader.TryRead(source, out WireFrameHeader header);

        Assert.False(success);
        Assert.Equal(default, header);
    }

    [Fact]
    public void Write_WritesLittleEndianHeader()
    {
        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameHeader.Write(destination, length: 0x1234, packetId: 0x5678);

        Assert.Equal([0x34, 0x12, 0x78, 0x56], destination.ToArray());
    }

    [Fact]
    public void Write_OnlyModifiesHeaderRegion()
    {
        Span<byte> destination = stackalloc byte[6];
        destination.Fill(0xCC);

        WireFrameHeader.Write(destination, length: 4, packetId: 0x1234);

        Assert.Equal([0x04, 0x00, 0x34, 0x12, 0xCC, 0xCC], destination.ToArray());
    }

    [Fact]
    public void Write_AcceptsZeroLengthAndPacketIdAsRawHeaderValues()
    {
        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameHeader.Write(destination, length: 0, packetId: 0);

        Assert.Equal([0x00, 0x00, 0x00, 0x00], destination.ToArray());
    }

    [Fact]
    public void Write_AcceptsMaximumUInt16Values()
    {
        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameHeader.Write(destination, ushort.MaxValue, ushort.MaxValue);

        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF], destination.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Write_RejectsUndersizedDestinationWithoutModifyingIt(int destinationLength)
    {
        byte[] destination = Enumerable.Repeat((byte)0xCC, destinationLength).ToArray();

        byte[] expected = destination.ToArray();

        try
        {
            WireFrameHeader.Write(destination, length: 4, packetId: 0x1234);

            Assert.Fail("Expected the undersized destination to be rejected.");
        }
        catch (ArgumentException exception)
        {
            Assert.Equal("destination", exception.ParamName);

            Assert.StartsWith(
                $"Destination must contain at least {WireFrameHeader.Size} bytes.",
                exception.Message
            );
        }

        Assert.Equal(expected, destination);
    }

    [Fact]
    public void WriteAndTryRead_RoundTripExactly()
    {
        Span<byte> destination = stackalloc byte[WireFrameHeader.Size];

        WireFrameHeader.Write(destination, length: 0x3456, packetId: 0x789A);

        bool success = WireFrameHeader.TryRead(destination, out WireFrameHeader header);

        Assert.True(success);
        Assert.Equal(0x3456, header.Length);
        Assert.Equal(0x789A, header.PacketId);
    }
}
