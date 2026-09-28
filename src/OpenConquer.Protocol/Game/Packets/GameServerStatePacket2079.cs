using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client server-state packet.
/// </summary>
public sealed class GameServerStatePacket2079(uint state) : IPacket
{
    public const ushort PacketIdentifier = 2079;
    public const int PayloadSize = sizeof(uint);

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint State { get; } = state;

    public void WritePayload(ref PacketWriter writer) => writer.WriteUInt32(State);
}
