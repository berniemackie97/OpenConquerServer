namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

internal sealed class WeaponSkillRecord
{
    public uint OwnerCharacterId { get; set; }
    public uint WeaponSkillType { get; set; }
    public byte Level { get; set; }
    public uint Experience { get; set; }
}
