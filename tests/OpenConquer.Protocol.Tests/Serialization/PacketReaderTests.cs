using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Tests.Serialization;

public sealed class PacketReaderTests
{
    [Fact]
    public void Constructor_StartsAtBeginning()
    {
        ReadOnlySpan<byte> buffer = [0xAA, 0xBB];

        PacketReader reader = new(buffer);

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
        Assert.False(reader.ConsumedAll);
    }

    [Fact]
    public void EmptyBuffer_StartsConsumed()
    {
        PacketReader reader = new([]);

        Assert.Equal(0, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadByte_ReadsValueAndAdvances()
    {
        PacketReader reader = new([0x7F]);

        byte value = reader.ReadByte();

        Assert.Equal(0x7F, value);
        Assert.Equal(1, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadUInt16_ReadsLittleEndian()
    {
        PacketReader reader = new([0x34, 0x12]);

        ushort value = reader.ReadUInt16();

        Assert.Equal(0x1234, value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadUInt32_ReadsLittleEndian()
    {
        PacketReader reader = new([0xEF, 0xCD, 0xAB, 0x89]);

        uint value = reader.ReadUInt32();

        Assert.Equal(0x89ABCDEFu, value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadBytes_ReadsExactCountAndAdvances()
    {
        PacketReader reader = new([0x10, 0x20, 0x30, 0x40]);

        ReadOnlySpan<byte> value = reader.ReadBytes(3);

        Assert.Equal([0x10, 0x20, 0x30], value.ToArray());

        Assert.Equal(3, reader.Position);
        Assert.Equal(1, reader.Remaining);
        Assert.False(reader.ConsumedAll);
    }

    [Fact]
    public void ReadBytes_AllowsZeroLength()
    {
        PacketReader reader = new([0xAA]);

        ReadOnlySpan<byte> value = reader.ReadBytes(0);

        Assert.True(value.IsEmpty);
        Assert.Equal(0, reader.Position);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void ReadBytes_ThrowsForNegativeCountWithoutAdvancing()
    {
        PacketReader reader = new([0xAA]);

        try
        {
            reader.ReadBytes(-1);
            Assert.Fail("Expected a negative byte count to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("count", exception.ParamName);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void ReadFixedString_StopsAtFirstNullButConsumesEntireField()
    {
        PacketReader reader = new([(byte)'A', (byte)'B', 0x00, (byte)'C', 0x00, 0x7F]);

        string value = reader.ReadFixedString(5);

        Assert.Equal("AB", value);
        Assert.Equal(5, reader.Position);
        Assert.Equal(1, reader.Remaining);
        Assert.Equal(0x7F, reader.ReadByte());
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_UsesEntireFieldWhenNoNullTerminatorExists()
    {
        PacketReader reader = new([(byte)'A', (byte)'B', (byte)'C']);

        string value = reader.ReadFixedString(3);

        Assert.Equal("ABC", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_UsesAnsiByDefault()
    {
        PacketReader reader = new([0x80, 0x00]);

        string value = reader.ReadFixedString(2);

        Assert.Equal("€", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_UsesAscii()
    {
        PacketReader reader = new([(byte)'A', (byte)'B', 0x00]);

        string value = reader.ReadFixedString(3, TqTextEncoding.Ascii);

        Assert.Equal("AB", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_AsciiDiffersFromAnsiForExtendedByte()
    {
        PacketReader ansiReader = new([0x80]);
        PacketReader asciiReader = new([0x80]);

        string ansi = ansiReader.ReadFixedString(1, TqTextEncoding.Ansi);
        string ascii = asciiReader.ReadFixedString(1, TqTextEncoding.Ascii);

        Assert.Equal("€", ansi);
        Assert.Equal("?", ascii);

        Assert.True(ansiReader.ConsumedAll);
        Assert.True(asciiReader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_UsesStrictAnsi()
    {
        PacketReader reader = new([0x80, 0x00]);

        string value = reader.ReadFixedString(2, TqTextEncoding.StrictAnsi);

        Assert.Equal("€", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadFixedString_AllowsZeroWidth()
    {
        PacketReader reader = new([0xAA]);

        string value = reader.ReadFixedString(0);

        Assert.Equal(string.Empty, value);
        Assert.Equal(0, reader.Position);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void ReadFixedString_ThrowsForNegativeWidthWithoutAdvancing()
    {
        PacketReader reader = new([0xAA]);

        try
        {
            reader.ReadFixedString(-1);

            Assert.Fail("Expected a negative field width to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("width", exception.ParamName);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void ReadFixedString_RejectsUnknownEncodingWithoutAdvancing()
    {
        PacketReader reader = new([(byte)'A', 0x00]);

        try
        {
            reader.ReadFixedString(2, (TqTextEncoding)int.MaxValue);

            Assert.Fail("Expected an unknown TQ text encoding to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("encoding", exception.ParamName);
            Assert.StartsWith("Unknown TQ text encoding.", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void ReadFixedString_UnderflowDoesNotAdvance()
    {
        PacketReader reader = new([(byte)'A', (byte)'B']);

        try
        {
            reader.ReadFixedString(3);

            Assert.Fail("Expected the fixed-width field to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void ReadByteString_ReadsAnsiByDefault()
    {
        PacketReader reader = new([0x01, 0x80]);

        string value = reader.ReadByteString();

        Assert.Equal("€", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadByteString_ReadsEntireDeclaredLength()
    {
        PacketReader reader = new([0x03, (byte)'A', (byte)'B', (byte)'C', 0x7F]);

        string value = reader.ReadByteString();

        Assert.Equal("ABC", value);
        Assert.Equal(4, reader.Position);
        Assert.Equal(1, reader.Remaining);
        Assert.Equal(0x7F, reader.ReadByte());
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadByteString_UsesAscii()
    {
        PacketReader reader = new([0x02, (byte)'A', (byte)'B']);

        string value = reader.ReadByteString(TqTextEncoding.Ascii);

        Assert.Equal("AB", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadByteString_UsesStrictAnsi()
    {
        PacketReader reader = new([0x01, 0x80]);

        string value = reader.ReadByteString(TqTextEncoding.StrictAnsi);

        Assert.Equal("€", value);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void ReadByteString_ZeroLengthReturnsEmptyString()
    {
        PacketReader reader = new([0x00, 0x7F]);

        string value = reader.ReadByteString();

        Assert.Equal(string.Empty, value);
        Assert.Equal(1, reader.Position);
        Assert.Equal(1, reader.Remaining);
        Assert.Equal(0x7F, reader.ReadByte());
    }

    [Fact]
    public void ReadByteString_RejectsUnknownEncodingWithoutAdvancing()
    {
        PacketReader reader = new([0x01, (byte)'A']);

        try
        {
            reader.ReadByteString((TqTextEncoding)int.MaxValue);

            Assert.Fail("Expected an unknown TQ text encoding to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("encoding", exception.ParamName);
            Assert.StartsWith("Unknown TQ text encoding.", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void ReadByteString_InvalidEncodingWinsBeforeBufferValidation()
    {
        PacketReader reader = new([]);

        try
        {
            reader.ReadByteString((TqTextEncoding)int.MaxValue);

            Assert.Fail("Expected an unknown TQ text encoding to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("encoding", exception.ParamName);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadByteString_MissingLengthByteDoesNotAdvance()
    {
        PacketReader reader = new([]);

        try
        {
            reader.ReadByteString();

            Assert.Fail("Expected the missing length byte to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadByteString_TruncatedValueDoesNotAdvance()
    {
        PacketReader reader = new([0x03, (byte)'A']);

        try
        {
            reader.ReadByteString();

            Assert.Fail("Expected the byte string to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void PrimitiveUnderflow_DoesNotAdvance()
    {
        PacketReader reader = new([0xAA]);

        try
        {
            reader.ReadUInt16();

            Assert.Fail("Expected UInt16 read to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(1, reader.Remaining);
    }

    [Fact]
    public void ReadBytes_UnderflowDoesNotAdvance()
    {
        PacketReader reader = new([0xAA, 0xBB]);

        try
        {
            reader.ReadBytes(3);

            Assert.Fail("Expected the byte read to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }

        Assert.Equal(0, reader.Position);
        Assert.Equal(2, reader.Remaining);
    }

    [Fact]
    public void SequentialReads_UpdateCursorDeterministically()
    {
        PacketReader reader = new([0xAA, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89]);

        Assert.Equal(0xAA, reader.ReadByte());

        Assert.Equal(1, reader.Position);
        Assert.Equal(6, reader.Remaining);

        Assert.Equal(0x1234, reader.ReadUInt16());

        Assert.Equal(3, reader.Position);
        Assert.Equal(4, reader.Remaining);

        Assert.Equal(0x89ABCDEFu, reader.ReadUInt32());

        Assert.Equal(7, reader.Position);
        Assert.Equal(0, reader.Remaining);
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void FailedReadAfterSuccessfulReads_PreservesCommittedPosition()
    {
        PacketReader reader = new([0xAA, 0xBB]);

        Assert.Equal(0xAA, reader.ReadByte());

        try
        {
            reader.ReadUInt16();

            Assert.Fail("Expected UInt16 read to underflow.");
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, reader.Position);
        Assert.Equal(1, reader.Remaining);
        Assert.Equal(0xBB, reader.ReadByte());
        Assert.True(reader.ConsumedAll);
    }

    [Fact]
    public void UnderflowMessage_IsStable()
    {
        PacketReader reader = new([]);

        try
        {
            reader.ReadByte();

            Assert.Fail("Expected the empty reader to underflow.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("PacketReader: buffer underflow", exception.Message);
        }
    }
}
