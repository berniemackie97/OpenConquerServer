using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes a weapon-skill or magic experience update (packet 1104).
/// </summary>
public sealed class GameSkillExperiencePacket(uint experience, uint nextLevelExperienceRequirement, ushort skillOrMagicType, byte action) : IPacket
{
    public const ushort PacketIdentifier = 1104;
    public const int FixedPacketLength = 16;
    public const int PayloadSize = FixedPacketLength - 4;
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
            throw new InvalidOperationException($"Skill experience payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
