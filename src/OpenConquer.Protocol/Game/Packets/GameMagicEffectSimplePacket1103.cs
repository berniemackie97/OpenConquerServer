using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes the verified native 5517 MsgMagicEffectSimple packet-1103 layout.
/// </summary>
public sealed class GameMagicEffectSimplePacket1103(uint ownerCharacterId, ushort magicType, ushort level) : IPacket
{
    public const ushort PacketIdentifier = 1103;
    public const int FixedPacketLength = 12;
    public const int PayloadSize = FixedPacketLength - 4;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint OwnerCharacterId { get; } = ownerCharacterId;
    public ushort MagicType { get; } = magicType;
    public ushort Level { get; } = level;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(OwnerCharacterId);
        writer.WriteUInt16(MagicType);
        writer.WriteUInt16(Level);

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"MsgMagicEffectSimple payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
