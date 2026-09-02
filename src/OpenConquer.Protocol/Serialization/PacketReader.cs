using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Serialization;

/// <summary>
/// Reads primitive values and TQ protocol strings sequentially from packet data.
/// </summary>
public ref struct PacketReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buffer = buffer;
    private int _offset;

    public readonly int Position => _offset;
    public readonly int Remaining => _buffer.Length - _offset;
    public readonly bool ConsumedAll => _offset == _buffer.Length;

    public byte ReadByte()
    {
        return ReadSpan(count: sizeof(byte))[0];
    }

    public ushort ReadUInt16()
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(source: ReadSpan(count: sizeof(ushort)));
    }

    public uint ReadUInt32()
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(source: ReadSpan(count: sizeof(uint)));
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        return ReadSpan(count);
    }

    /// <summary>
    /// Reads a fixed width, zero padded string using the selected TQ text encoding.
    /// </summary>
    public string ReadFixedString(int width, TqTextEncoding encoding = TqTextEncoding.Ansi)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);

        Encoding selectedEncoding = TqEncoding.Resolve(encoding);
        ReadOnlySpan<byte> field = PeekSpan(width);
        int terminatorIndex = field.IndexOf((byte)0);

        ReadOnlySpan<byte> value = terminatorIndex >= 0 ? field[..terminatorIndex] : field;
        string result = selectedEncoding.GetString(value);
        _offset += width;

        return result;
    }

    /// <summary>
    /// Reads a TQ byte length prefixed string using the selected TQ text encoding.
    /// </summary>
    public string ReadByteString(TqTextEncoding encoding = TqTextEncoding.Ansi)
    {
        Encoding selectedEncoding = TqEncoding.Resolve(encoding);

        ReadOnlySpan<byte> lengthField = PeekSpan(count: sizeof(byte));
        byte length = lengthField[0];
        int fieldLength = sizeof(byte) + length;
        ReadOnlySpan<byte> field = PeekSpan(fieldLength);

        if (length == 0)
        {
            _offset += fieldLength;
            return string.Empty;
        }

        string result = selectedEncoding.GetString(field[sizeof(byte)..]);
        _offset += fieldLength;

        return result;
    }

    private ReadOnlySpan<byte> ReadSpan(int count)
    {
        ReadOnlySpan<byte> value = PeekSpan(count);

        _offset += count;

        return value;
    }

    private readonly ReadOnlySpan<byte> PeekSpan(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > Remaining)
        {
            throw new InvalidOperationException("PacketReader: buffer underflow");
        }

        return _buffer.Slice(start: _offset, length: count);
    }
}
