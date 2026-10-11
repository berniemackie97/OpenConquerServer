using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Framing;

/// <summary>
/// Encodes GameServer packets while enforcing the client's
/// maximum accepted packet length.
/// </summary>
public static class GameWireFrameEncoder
{
    public static int GetFrameLength(IPacket packet) => WireFrameEncoder.GetFrameLength(packet, GameWireProtocol.MaximumPacketLength);

    public static int WriteFrame(IPacket packet, Span<byte> destination) => WireFrameEncoder.WriteFrame(packet, destination, GameWireProtocol.MaximumPacketLength);
}
