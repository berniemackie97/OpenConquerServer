using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Represents the server -> client 5517 login seed packet.
/// </summary>
public sealed class LoginSeedPacket(uint seed) : IPacket
{
    public const ushort PacketIdentifier = 1059;
    public const int PayloadSize = sizeof(uint);

    public ushort PacketId => PacketIdentifier;

    public int PayloadLength => PayloadSize;

    public uint Seed { get; } = seed;

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(Seed);
    }
}
