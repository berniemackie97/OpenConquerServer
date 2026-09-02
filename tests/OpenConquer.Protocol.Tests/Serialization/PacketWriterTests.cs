using System.Text;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Tests.Serialization;

public sealed class PacketWriterTests
{
    [Fact]
    public void Constructor_StartsEmpty()
    {
        Span<byte> buffer = stackalloc byte[8];
        PacketWriter writer = new(buffer);

        Assert.Equal(0, writer.Written);
        Assert.Equal(8, writer.Remaining);
        Assert.True(writer.WrittenSpan.IsEmpty);
    }

    [Fact]
    public void WriteByte_WritesValueAndAdvances()
    {
        Span<byte> buffer = stackalloc byte[1];
        PacketWriter writer = new(buffer);

        writer.WriteByte(0x7F);

        Assert.Equal(1, writer.Written);
        Assert.Equal(0, writer.Remaining);
        Assert.Equal([0x7F], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteUInt16_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        PacketWriter writer = new(buffer);

        writer.WriteUInt16(0x1234);

        Assert.Equal([0x34, 0x12], writer.WrittenSpan.ToArray());

        Assert.Equal(sizeof(ushort), writer.Written);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void WriteUInt32_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        PacketWriter writer = new(buffer);

        writer.WriteUInt32(0x89ABCDEFu);

        Assert.Equal([0xEF, 0xCD, 0xAB, 0x89], writer.WrittenSpan.ToArray());

        Assert.Equal(sizeof(uint), writer.Written);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void WriteUInt64_WritesLittleEndian()
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        PacketWriter writer = new(buffer);

        writer.WriteUInt64(0x0123456789ABCDEFul);

        Assert.Equal(
            [0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01],
            writer.WrittenSpan.ToArray()
        );

        Assert.Equal(sizeof(ulong), writer.Written);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void WriteBytes_WritesAllBytes()
    {
        Span<byte> buffer = stackalloc byte[4];
        PacketWriter writer = new(buffer);

        writer.WriteBytes([0x10, 0x20, 0x30, 0x40]);

        Assert.Equal([0x10, 0x20, 0x30, 0x40], writer.WrittenSpan.ToArray());

        Assert.Equal(4, writer.Written);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void WriteBytes_DoesNothingForEmptyInput()
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = 0xAA;

        PacketWriter writer = new(buffer);

        writer.WriteBytes([]);

        Assert.Equal(0, writer.Written);
        Assert.Equal(1, writer.Remaining);
        Assert.True(writer.WrittenSpan.IsEmpty);
        Assert.Equal(0xAA, buffer[0]);
    }

    [Fact]
    public void WriteBytes_SupportsOverlappingSourceAndDestination()
    {
        Span<byte> buffer = [0xAA, 0xBB, 0xCC, 0xDD];

        PacketWriter writer = new(buffer);

        writer.WriteByte(0xAA);

        ReadOnlySpan<byte> source = buffer[..3];

        writer.WriteBytes(source);

        Assert.Equal([0xAA, 0xAA, 0xBB, 0xCC], buffer.ToArray());

        Assert.Equal(4, writer.Written);
        Assert.Equal(0, writer.Remaining);
    }

    [Fact]
    public void Reserve_WritesZeroFilledBytes()
    {
        Span<byte> buffer = stackalloc byte[5];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.WriteByte(0xAA);
        writer.Reserve(3);
        writer.WriteByte(0xBB);

        Assert.Equal([0xAA, 0x00, 0x00, 0x00, 0xBB], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Reserve_ZeroesCallerOwnedMemory()
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.Reserve(4);

        Assert.Equal([0x00, 0x00, 0x00, 0x00], buffer.ToArray());
    }

    [Fact]
    public void Reserve_DoesNothingForZero()
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = 0xAA;

        PacketWriter writer = new(buffer);

        writer.Reserve(0);

        Assert.Equal(0, writer.Written);
        Assert.Equal(1, writer.Remaining);
        Assert.Equal(0xAA, buffer[0]);
    }

    [Fact]
    public void Reserve_ThrowsForNegativeCount()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            ReserveNegativeCount
        );

        Assert.Equal("count", exception.ParamName);
    }

    [Fact]
    public void WriteFixedString_UsesAnsiByDefaultAndZeroPads()
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.WriteFixedString("€", 4);

        Assert.Equal([0x80, 0x00, 0x00, 0x00], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFixedString_DefaultAnsiUsesRuntimeFallback()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.WriteFixedString("漢", 2);

        Assert.Equal([(byte)'?', 0x00], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_DefaultAnsiUsesRuntimeFallback()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.WriteByteString("漢");

        Assert.Equal([0x01, (byte)'?'], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFixedString_UsesAscii()
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        writer.WriteFixedString("AB", 4, TqTextEncoding.Ascii);

        Assert.Equal([(byte)'A', (byte)'B', 0x00, 0x00], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFixedString_UsesStrictAnsi()
    {
        Span<byte> buffer = stackalloc byte[2];
        PacketWriter writer = new(buffer);

        writer.WriteFixedString("€", 2, TqTextEncoding.StrictAnsi);

        Assert.Equal([0x80, 0x00], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFixedString_ConsumesEntireFieldWidth()
    {
        Span<byte> buffer = stackalloc byte[6];
        PacketWriter writer = new(buffer);

        writer.WriteFixedString("AB", 5);
        writer.WriteByte(0x7F);

        Assert.Equal([(byte)'A', (byte)'B', 0x00, 0x00, 0x00, 0x7F], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFixedString_AllowsEmptyZeroWidthField()
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = 0xAA;

        PacketWriter writer = new(buffer);

        writer.WriteFixedString("", 0);

        Assert.Equal(0, writer.Written);
        Assert.Equal(1, writer.Remaining);
        Assert.Equal(0xAA, buffer[0]);
    }

    [Fact]
    public void WriteFixedString_ThrowsForNullValueWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString(null!, 3);

            Assert.Fail("Expected a null value to be rejected.");
        }
        catch (ArgumentNullException exception)
        {
            Assert.Equal("value", exception.ParamName);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(3, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_ThrowsForNegativeWidthWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString("A", -1);

            Assert.Fail("Expected a negative field width to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("width", exception.ParamName);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(3, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_RejectsEmbeddedNullWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[5];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString("A\0B", 5);

            Assert.Fail("Expected an embedded null character to be rejected.");
        }
        catch (ArgumentException exception)
        {
            Assert.Equal("value", exception.ParamName);

            Assert.StartsWith(
                "Fixed-width TQ strings must not contain embedded null characters.",
                exception.Message
            );
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(5, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_RejectsUnknownEncodingWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString("A", 4, (TqTextEncoding)int.MaxValue);

            Assert.Fail("Expected an unknown TQ text encoding to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("encoding", exception.ParamName);

            Assert.StartsWith("Unknown TQ text encoding.", exception.Message);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(4, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_RejectsValueWiderThanFieldWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString("AB", 1);

            Assert.Fail("Expected the value to exceed the fixed field width.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("value", exception.ParamName);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(2, writer.Remaining);

        Assert.Equal([0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_StrictEncodingFailureDoesNotModifyWriter()
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteFixedString("漢", 4, TqTextEncoding.StrictAnsi);

            Assert.Fail("Expected the unsupported character to be rejected.");
        }
        catch (EncoderFallbackException) { }

        Assert.Equal(0, writer.Written);
        Assert.Equal(4, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteFixedString_OverflowDoesNotModifyBuffer()
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);
        writer.WriteByte(0xAA);

        try
        {
            writer.WriteFixedString("AB", 3);

            Assert.Fail("Expected the fixed-width field to exceed the remaining capacity.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 3 bytes with 2 remaining.",
                exception.Message
            );
        }

        Assert.Equal(1, writer.Written);
        Assert.Equal(2, writer.Remaining);

        Assert.Equal([0xAA, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteByteString_WritesAnsiLengthPrefixedString()
    {
        Span<byte> buffer = stackalloc byte[2];
        PacketWriter writer = new(buffer);

        writer.WriteByteString("€");

        Assert.Equal([0x01, 0x80], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_UsesAscii()
    {
        Span<byte> buffer = stackalloc byte[3];
        PacketWriter writer = new(buffer);

        writer.WriteByteString("AB", TqTextEncoding.Ascii);

        Assert.Equal([0x02, (byte)'A', (byte)'B'], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_UsesStrictAnsi()
    {
        Span<byte> buffer = stackalloc byte[2];
        PacketWriter writer = new(buffer);

        writer.WriteByteString("€", TqTextEncoding.StrictAnsi);

        Assert.Equal([0x01, 0x80], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_WritesZeroLengthForEmptyValue()
    {
        Span<byte> buffer = stackalloc byte[1];
        PacketWriter writer = new(buffer);

        writer.WriteByteString("");

        Assert.Equal([0x00], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_AllowsEmbeddedNull()
    {
        Span<byte> buffer = stackalloc byte[4];
        PacketWriter writer = new(buffer);

        writer.WriteByteString("A\0B");

        Assert.Equal([0x03, (byte)'A', 0x00, (byte)'B'], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteString_AllowsMaximumEncodedLength()
    {
        string value = new('A', byte.MaxValue);

        byte[] buffer = new byte[sizeof(byte) + byte.MaxValue];

        PacketWriter writer = new(buffer);

        writer.WriteByteString(value);

        Assert.Equal(sizeof(byte) + byte.MaxValue, writer.Written);

        Assert.Equal(byte.MaxValue, buffer[0]);

        Assert.All(buffer[1..], valueByte => Assert.Equal((byte)'A', valueByte));
    }

    [Fact]
    public void WriteByteString_ThrowsForNullValueWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteByteString(null!);

            Assert.Fail("Expected a null value to be rejected.");
        }
        catch (ArgumentNullException exception)
        {
            Assert.Equal("value", exception.ParamName);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(3, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteByteString_RejectsUnknownEncodingWithoutModifyingWriter()
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteByteString("A", (TqTextEncoding)int.MaxValue);

            Assert.Fail("Expected an unknown TQ text encoding to be rejected.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("encoding", exception.ParamName);

            Assert.StartsWith("Unknown TQ text encoding.", exception.Message);
        }

        Assert.Equal(0, writer.Written);
        Assert.Equal(3, writer.Remaining);

        Assert.Equal([0xCC, 0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteByteString_RejectsEncodedLengthAboveByteMaximum()
    {
        string value = new('A', byte.MaxValue + 1);

        byte[] buffer = Enumerable.Repeat((byte)0xCC, byte.MaxValue + 2).ToArray();

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteByteString(value);

            Assert.Fail("Expected the encoded string to exceed the one-byte length prefix.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            Assert.Equal("value", exception.ParamName);
        }

        Assert.Equal(0, writer.Written);

        Assert.All(buffer, valueByte => Assert.Equal(0xCC, valueByte));
    }

    [Fact]
    public void WriteByteString_StrictEncodingFailureDoesNotModifyWriter()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);

        try
        {
            writer.WriteByteString("漢", TqTextEncoding.StrictAnsi);

            Assert.Fail("Expected the unsupported character to be rejected.");
        }
        catch (EncoderFallbackException) { }

        Assert.Equal(0, writer.Written);
        Assert.Equal(2, writer.Remaining);

        Assert.Equal([0xCC, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void WriteByteString_OverflowDoesNotModifyBuffer()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);
        writer.WriteByte(0xAA);

        try
        {
            writer.WriteByteString("A");

            Assert.Fail("Expected the byte string to exceed the remaining capacity.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 2 bytes with 1 remaining.",
                exception.Message
            );
        }

        Assert.Equal(1, writer.Written);
        Assert.Equal(1, writer.Remaining);

        Assert.Equal([0xAA, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void Writer_AllowsExactCapacity()
    {
        Span<byte> buffer = stackalloc byte[7];
        PacketWriter writer = new(buffer);

        writer.WriteByte(0xAA);
        writer.WriteUInt16(0x1234);
        writer.WriteUInt32(0x89ABCDEF);

        Assert.Equal(7, writer.Written);
        Assert.Equal(0, writer.Remaining);

        Assert.Equal([0xAA, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89], writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByte_ThrowsWhenBufferIsFull()
    {
        Span<byte> buffer = stackalloc byte[1];
        PacketWriter writer = new(buffer);

        writer.WriteByte(0xAA);

        try
        {
            writer.WriteByte(0xBB);

            Assert.Fail("Expected the full writer to reject another byte.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 1 bytes with 0 remaining.",
                exception.Message
            );
        }

        Assert.Equal(1, writer.Written);
        Assert.Equal(0, writer.Remaining);
        Assert.Equal(0xAA, buffer[0]);
    }

    [Fact]
    public void FailedWrite_DoesNotAdvanceWriter()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);
        writer.WriteByte(0xAA);

        try
        {
            writer.WriteUInt16(0x1234);

            Assert.Fail("Expected the write to exceed the remaining capacity.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 2 bytes with 1 remaining.",
                exception.Message
            );
        }

        Assert.Equal(1, writer.Written);
        Assert.Equal(1, writer.Remaining);

        Assert.Equal([0xAA, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void Reserve_OverflowDoesNotModifyBuffer()
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer.Fill(0xCC);

        PacketWriter writer = new(buffer);
        writer.WriteByte(0xAA);

        try
        {
            writer.Reserve(2);

            Assert.Fail("Expected the reservation to exceed the remaining capacity.");
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(
                "PacketWriter buffer overflow: requested 2 bytes with 1 remaining.",
                exception.Message
            );
        }

        Assert.Equal(1, writer.Written);
        Assert.Equal(1, writer.Remaining);

        Assert.Equal([0xAA, 0xCC], buffer.ToArray());
    }

    [Fact]
    public void EmptyBuffer_AllowsZeroLengthOperations()
    {
        Span<byte> buffer = [];
        PacketWriter writer = new(buffer);

        writer.WriteBytes([]);
        writer.Reserve(0);
        writer.WriteFixedString("", 0);

        Assert.Equal(0, writer.Written);
        Assert.Equal(0, writer.Remaining);
        Assert.True(writer.WrittenSpan.IsEmpty);
    }

    private static void ReserveNegativeCount()
    {
        Span<byte> buffer = stackalloc byte[1];
        PacketWriter writer = new(buffer);

        writer.Reserve(-1);
    }
}
