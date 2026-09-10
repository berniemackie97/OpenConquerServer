using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Packets;

namespace OpenConquer.Protocol.Game;

/// <summary>
/// Encodes 5517 GameServer packets while enforcing the native client's
/// maximum accepted packet length.
/// </summary>
public static class GameWireFrameEncoder
{
    public static int GetFrameLength(IPacket packet) => WireFrameEncoder.GetFrameLength(packet, GameWireProtocol.MaximumPacketLength);

    public static int WriteFrame(IPacket packet, Span<byte> destination) => WireFrameEncoder.WriteFrame(packet, destination, GameWireProtocol.MaximumPacketLength);
}
