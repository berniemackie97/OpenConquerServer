

namespace OpenConquer.Protocol.Serialization;

/// <summary>
/// Defines a packet that can be encoded into a wire frame.
/// </summary>
public interface IPacket
{
    ushort PacketId { get; }
    int PayloadLength { get; }
    void WritePayload(ref PacketWriter writer);
}
