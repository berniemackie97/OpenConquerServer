using OpenConquer.Domain.Characters;

namespace OpenConquer.Domain.Skills;

/// <summary>
/// Represents one weapon proficiency owned by a character.
/// </summary>
public readonly record struct WeaponSkill
{
    private WeaponSkill(uint ownerCharacterId, uint type, byte level, uint experience)
    {
        OwnerCharacterId = ownerCharacterId;
        Type = type;
        Level = level;
        Experience = experience;
    }

    public uint OwnerCharacterId { get; }
    public uint Type { get; }
    public byte Level { get; }
    public uint Experience { get; }
    public uint NextLevelExperienceRequirement => WeaponSkillExperienceCurve.GetNextLevelExperienceRequirement(Level);
    public bool IsValid => CharacterIdentityPolicy.IsPlayerEntityId(OwnerCharacterId) && Level <= WeaponSkillExperienceCurve.MaximumLevel;

    public static WeaponSkill Create(uint ownerCharacterId, uint type, byte level, uint experience)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(ownerCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerCharacterId), ownerCharacterId, "Weapon-skill owner must identify a player character.");
        }

        if (level > WeaponSkillExperienceCurve.MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Weapon-skill level must be between 0 and {WeaponSkillExperienceCurve.MaximumLevel}.");
        }

        return new WeaponSkill(ownerCharacterId, type, level, experience);
    }
}
