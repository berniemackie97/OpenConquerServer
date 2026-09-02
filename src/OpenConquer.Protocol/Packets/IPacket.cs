using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Packets;

/// <summary>
/// Defines a packet that can be encoded into a TQ protocol frame.
/// </summary>
public interface IPacket
{
    /// <summary>
    /// Gets the protocol packet identifier.
    /// </summary>
    ushort PacketId { get; }

    /// <summary>
    /// Gets the encoded payload length, excluding the wire frame header.
    /// </summary>
    int PayloadLength { get; }

    /// <summary>
    /// Writes the packet payload without the wire frame header.
    /// </summary>
    void WritePayload(ref PacketWriter writer);
}
