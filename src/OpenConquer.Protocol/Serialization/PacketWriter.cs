using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Serialization;

/// <summary>
/// Writes primitive values and TQ protocol strings sequentially into caller owned memory.
/// </summary>
public ref struct PacketWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    private int _written;

    public readonly int Written => _written;
    public readonly int Remaining => _buffer.Length - _written;
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer[.._written];

    public void Reserve(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        Span<byte> destination = GetWritableSpan(count);
        destination.Clear();

        _written += count;
    }

    public void WriteByte(byte value)
    {
        Span<byte> destination = GetWritableSpan(count: sizeof(byte));
        destination[0] = value;

        _written += sizeof(byte);
    }

    public void WriteBytes(scoped ReadOnlySpan<byte> value)
    {
        Span<byte> destination = GetWritableSpan(value.Length);
        value.CopyTo(destination);

        _written += value.Length;
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> destination = GetWritableSpan(count: sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);

        _written += sizeof(ushort);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> destination = GetWritableSpan(count: sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);

        _written += sizeof(uint);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> destination = GetWritableSpan(count: sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(destination, value);

        _written += sizeof(ulong);
    }

    public void WriteFixedString(string value, int width, TqTextEncoding encoding = TqTextEncoding.Ansi)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(width);

        if (value.Contains('\0'))
        {
            throw new ArgumentException("Fixed-width TQ strings must not contain embedded null characters.", nameof(value));
        }

        Encoding selectedEncoding = TqEncoding.Resolve(encoding);
        int byteCount = selectedEncoding.GetByteCount(value);

        if (byteCount > width)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "String exceeds the fixed field width.");
        }

        Span<byte> destination = GetWritableSpan(width);
        destination.Clear();

        if (byteCount != 0)
        {
            selectedEncoding.GetBytes(value, destination[..byteCount]);
        }

        _written += width;
    }

    public void WriteByteString(string value, TqTextEncoding encoding = TqTextEncoding.Ansi)
    {
        ArgumentNullException.ThrowIfNull(value);

        Encoding selectedEncoding = TqEncoding.Resolve(encoding);
        int byteCount = selectedEncoding.GetByteCount(value);

        if (byteCount > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Byte-length string must not exceed 255 encoded bytes.");
        }

        int fieldLength = sizeof(byte) + byteCount;
        Span<byte> destination = GetWritableSpan(fieldLength);
        destination[0] = (byte)byteCount;

        if (byteCount != 0)
        {
            selectedEncoding.GetBytes(value, destination[sizeof(byte)..]);
        }

        _written += fieldLength;
    }

    private Span<byte> GetWritableSpan(int count)
    {
        if (count > Remaining)
        {
            throw new InvalidOperationException($"PacketWriter buffer overflow: requested {count} bytes with {Remaining} remaining.");
        }

        return _buffer.Slice(start: _written, length: count);
    }
}
