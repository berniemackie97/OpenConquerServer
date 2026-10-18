using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// 5517 trade-quit request (1056, mode 2). The client also sends it when it dies. Trading itself isn't
/// implemented.
/// </summary>
public readonly record struct GameTradeQuitRequest(uint EntityId)
{
    public const ushort PacketIdentifier = 1056;
    public const ushort QuitMode = 2;
    public const int PacketLength = 12;

    public static bool TryParse(GameInboundFrame frame, out GameTradeQuitRequest request)
    {
        ArgumentNullException.ThrowIfNull(frame);
        request = default;
        if (frame.PacketId != PacketIdentifier || frame.Header.Length != PacketLength)
        {
            return false;
        }

        ReadOnlySpan<byte> wire = frame.Packet.Span;
        uint entityId = BinaryPrimitives.ReadUInt32LittleEndian(wire[4..]);
        ushort mode = BinaryPrimitives.ReadUInt16LittleEndian(wire[8..]);
        if (entityId == 0 || mode != QuitMode)
        {
            return false;
        }

        // The client is only known to use +4 and +8. +10..11 are unknown, so keep them as they are instead of
        // requiring zero.
        request = new GameTradeQuitRequest(entityId);
        return true;
    }
}
