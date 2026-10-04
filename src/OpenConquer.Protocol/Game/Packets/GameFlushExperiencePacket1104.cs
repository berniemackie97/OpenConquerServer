using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes the canonical 16-byte native-5517-compatible MsgFlushExp packet-1104 form.
/// </summary>
public sealed class GameFlushExperiencePacket1104(uint experience, uint nextLevelExperienceRequirement, ushort skillOrMagicType, byte action) : IPacket
{
    public const ushort PacketIdentifier = 1104;
    public const int CanonicalPacketLength = 16;
    public const int PayloadSize = CanonicalPacketLength - 4;
    public const byte WeaponSkillAction = 0;
    public const byte MagicAction = 1;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint Experience { get; } = experience;
    public uint NextLevelExperienceRequirement { get; } = nextLevelExperienceRequirement;
    public ushort SkillOrMagicType { get; } = skillOrMagicType;
    public byte Action { get; } = action;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(Experience);
        writer.WriteUInt32(NextLevelExperienceRequirement);
        writer.WriteUInt16(SkillOrMagicType);
        writer.WriteByte(Action);
        writer.Reserve(1);

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"Canonical MsgFlushExp payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
