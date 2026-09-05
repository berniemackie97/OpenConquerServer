using System.Buffers.Binary;

namespace OpenConquer.Protocol.Framing;

/// <summary>
/// Represents the raw 4 byte TQ protocol frame header.
/// </summary>
public readonly struct WireFrameHeader(ushort length, ushort packetId)
{
    public const int Size = sizeof(ushort) * 2;
    public ushort Length { get; } = length;
    public ushort PacketId { get; } = packetId;

    public static bool TryRead(ReadOnlySpan<byte> source, out WireFrameHeader header)
    {
        if (source.Length < Size)
        {
            header = default;
            return false;
        }

        header = new WireFrameHeader(length: BinaryPrimitives.ReadUInt16LittleEndian(source[..sizeof(ushort)]), packetId: BinaryPrimitives.ReadUInt16LittleEndian(source[sizeof(ushort)..Size]));

        return true;
    }

    public static void Write(Span<byte> destination, ushort length, ushort packetId)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException($"Destination must contain at least {Size} bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[..sizeof(ushort)], length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[sizeof(ushort)..Size], packetId);
    }
}
