using System.Collections.ObjectModel;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Application.Skills.Hydration;

public sealed class CharacterWeaponSkillSet
{
    private readonly ReadOnlyCollection<WeaponSkill> _skills;

    public CharacterWeaponSkillSet(uint characterId, IEnumerable<WeaponSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A weapon-skill set character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        WeaponSkill[] materializedSkills = skills.ToArray();
        HashSet<uint> skillTypes = new(materializedSkills.Length);

        foreach (WeaponSkill skill in materializedSkills)
        {
            if (!skill.IsValid)
            {
                throw new ArgumentException("A weapon-skill set cannot contain an invalid weapon skill.", nameof(skills));
            }

            if (skill.OwnerCharacterId != characterId)
            {
                throw new ArgumentException($"Weapon skill type {skill.Type} belongs to character {skill.OwnerCharacterId}, not weapon-skill-set character {characterId}.", nameof(skills));
            }

            if (!skillTypes.Add(skill.Type))
            {
                throw new ArgumentException($"Weapon-skill set contains duplicate weapon skill type {skill.Type}.", nameof(skills));
            }
        }

        Array.Sort(materializedSkills, static (left, right) => left.Type.CompareTo(right.Type));

        CharacterId = characterId;
        _skills = Array.AsReadOnly(materializedSkills);
    }

    public uint CharacterId
    {
        get;
    }
    public IReadOnlyList<WeaponSkill> Skills => _skills;
    public int Count => _skills.Count;
}
