using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes the verified native 5517 MsgWeaponSkill packet-1025 layout.
/// </summary>
public sealed class GameWeaponSkillPacket1025(uint weaponSkillType, ushort level, uint experience, uint nextLevelExperienceRequirement) : IPacket
{
    public const ushort PacketIdentifier = 1025;
    public const int FixedPacketLength = 20;
    public const int PayloadSize = FixedPacketLength - 4;

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint WeaponSkillType { get; } = weaponSkillType;
    public ushort Level { get; } = level;
    public uint Experience { get; } = experience;
    public uint NextLevelExperienceRequirement { get; } = nextLevelExperienceRequirement;

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(WeaponSkillType);
        writer.WriteUInt16(Level);
        writer.Reserve(sizeof(ushort));
        writer.WriteUInt32(Experience);
        writer.WriteUInt32(NextLevelExperienceRequirement);

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"MsgWeaponSkill payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
