using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Packets;

/// <summary>
/// Defines a packet that can be encoded into a TQ protocol frame.
/// </summary>
public interface IPacket
{
    ushort PacketId { get; }
    int PayloadLength { get; }
    void WritePayload(ref PacketWriter writer);
}
