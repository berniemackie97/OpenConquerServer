using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Server-to-client server state (packet 2079).
/// </summary>
public sealed class GameServerStatePacket(uint state) : IPacket
{
    public const ushort PacketIdentifier = 2079;
    public const int PayloadSize = sizeof(uint);

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint State { get; } = state;

    public void WritePayload(ref PacketWriter writer) => writer.WriteUInt32(State);
}
