using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents the native 5517 server-to-client map information packet.
/// </summary>
public sealed class GameMapInfoPacket1110(uint mapId, uint mapDataId, ulong flags) : IPacket
{
    public const ushort PacketIdentifier = 1110;
    public const int PayloadSize = sizeof(uint) * 4;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint MapId { get; } = mapId;
    public uint MapDataId { get; } = mapDataId;
    public ulong Flags { get; } = flags;

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(MapId);
        writer.WriteUInt32(MapDataId);
        writer.WriteUInt32((uint)(Flags & uint.MaxValue));
        writer.WriteUInt32((uint)(Flags >> 32));
    }
}
