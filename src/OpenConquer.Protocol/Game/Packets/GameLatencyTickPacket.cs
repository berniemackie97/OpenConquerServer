using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Packet 1009 subtype 27 is an 88-byte heartbeat with hero ID at +4 and the raw client timer at +16.
/// The server retains the received payload to echo it without modifying any bytes.
/// </summary>
public sealed class GameLatencyTickPacket : IPacket
{
    public const ushort PacketIdentifier = 1009;
    public const int FixedPacketLength = 88;
    public const ushort LatencyTickSubtype = 27;

    private const int SubtypeOffset = 12;

    private readonly byte[] _payload;

    private GameLatencyTickPacket(byte[] payload) => _payload = payload;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => _payload.Length;

    public static bool TryParse(GameInboundFrame frame, [NotNullWhen(true)] out GameLatencyTickPacket? packet)
    {
        ArgumentNullException.ThrowIfNull(frame);
        packet = null;
        if (frame.PacketId != PacketIdentifier || frame.Header.Length != FixedPacketLength)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = frame.Packet.Span;
        if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[SubtypeOffset..]) != LatencyTickSubtype)
        {
            return false;
        }

        packet = new GameLatencyTickPacket(frame.Payload.ToArray());
        return true;
    }

    public void WritePayload(ref PacketWriter writer) => writer.WriteBytes(_payload);
}
