using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// The 5517 client heartbeat, packet 1009 subtype 27: 88 bytes, +4 hero id, +12 subtype, +16 client time XOR hero id
/// (client <c>CMsgTick__Build_SubType</c> 0x6EAC51, sent every 10 s from 0x661B3B). The historical MsgServer answers it
/// by sending the same message back unchanged (<c>CMsgItem::Process</c> case 0x1B, 0x487F5E); this type keeps the
/// received payload so it can be echoed byte for byte.
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
