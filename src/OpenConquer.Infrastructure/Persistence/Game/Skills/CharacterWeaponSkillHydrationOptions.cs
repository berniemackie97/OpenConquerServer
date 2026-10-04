namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

public sealed class CharacterWeaponSkillHydrationOptions
{
    public const int DefaultMaximumSkillsPerCharacter = 4_096;
    public const int MaximumSupportedSkillsPerCharacter = 65_535;

    public CharacterWeaponSkillHydrationOptions(int maximumSkillsPerCharacter = DefaultMaximumSkillsPerCharacter)
    {
        if (maximumSkillsPerCharacter < 1 || maximumSkillsPerCharacter > MaximumSupportedSkillsPerCharacter)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSkillsPerCharacter), $"The character weapon-skill hydration limit must be between 1 and {MaximumSupportedSkillsPerCharacter}.");
        }

        MaximumSkillsPerCharacter = maximumSkillsPerCharacter;
    }

    public int MaximumSkillsPerCharacter { get; }
}
