using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Framing;

/// <summary>
/// Encodes protocol packets into complete TQ wire frames.
/// </summary>
public static class WireFrameEncoder
{
    /// <summary>
    /// Gets the complete encoded frame length using the full range supported by the 16 bit TQ frame length field.
    /// </summary>
    public static int GetFrameLength(IPacket packet)
    {
        return GetFrameLength(packet, maximumFrameLength: ushort.MaxValue);
    }

    /// <summary>
    /// Gets the complete encoded frame length, rejecting frames larger than
    /// <paramref name="maximumFrameLength"/>.
    /// </summary>
    public static int GetFrameLength(IPacket packet, int maximumFrameLength)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ValidateMaximumFrameLength(maximumFrameLength);

        ushort packetId = packet.PacketId;
        ValidatePacketId(packetId);

        int payloadLength = packet.PayloadLength;

        return GetValidatedFrameLength(packetId, payloadLength, maximumFrameLength);
    }

    /// <summary>
    /// Writes a complete TQ wire frame using the full range supported by the 16 bit TQ frame length field.
    /// </summary>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    public static int WriteFrame(IPacket packet, Span<byte> destination)
    {
        return WriteFrame(packet, destination, ushort.MaxValue);
    }

    /// <summary>
    /// Writes a complete TQ wire frame, rejecting frames larger than <paramref name="maximumFrameLength"/>.
    /// </summary>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    public static int WriteFrame(IPacket packet, Span<byte> destination, int maximumFrameLength)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ValidateMaximumFrameLength(maximumFrameLength);

        ushort packetId = packet.PacketId;
        ValidatePacketId(packetId);

        int payloadLength = packet.PayloadLength;
        int frameLength = GetValidatedFrameLength(packetId, payloadLength, maximumFrameLength);

        if (destination.Length < frameLength)
        {
            throw new ArgumentException($"Destination must contain at least {frameLength} bytes.", nameof(destination));
        }

        Span<byte> frame = destination[..frameLength];
        Span<byte> payload = frame[WireFrameHeader.Size..];

        try
        {
            PacketWriter writer = new(payload);
            packet.WritePayload(ref writer);

            if (writer.Written != payloadLength)
            {
                throw new InvalidOperationException($"Packet {packetId} declared a payload length of {payloadLength} bytes but wrote {writer.Written} bytes.");
            }

            WireFrameHeader.Write(frame[..WireFrameHeader.Size], (ushort)frameLength, packetId);

            return frameLength;
        }
        catch
        {
            frame.Clear();
            throw;
        }
    }

    private static void ValidatePacketId(ushort packetId)
    {
        if (packetId == 0)
        {
            throw new InvalidOperationException("Packet identifier 0 is invalid.");
        }
    }

    private static int GetValidatedFrameLength(ushort packetId, int payloadLength, int maximumFrameLength)
    {
        if (payloadLength < 0)
        {
            throw new InvalidOperationException($"Packet {packetId} declared a negative payload length.");
        }

        long frameLength = WireFrameHeader.Size + (long)payloadLength;

        if (frameLength > maximumFrameLength)
        {
            throw new InvalidOperationException($"Packet {packetId} declares a {frameLength}-byte frame, which exceeds the {maximumFrameLength}-byte maximum.");
        }

        return (int)frameLength;
    }

    private static void ValidateMaximumFrameLength(int maximumFrameLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFrameLength, WireFrameHeader.Size);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFrameLength, ushort.MaxValue);
    }
}
