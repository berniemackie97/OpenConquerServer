namespace OpenConquer.Domain.Skills;

/// <summary>
/// Provides the verified 5517 weapon-proficiency experience requirements sent to the client.
/// </summary>
public static class WeaponSkillExperienceCurve
{
    public const byte MaximumLevel = 20;

    private static readonly uint[] s_nextLevelExperienceRequirementByCurrentLevel =
    [
        0,
        1_200,
        68_000,
        250_000,
        640_000,
        1_600_000,
        4_000_000,
        10_000_000,
        22_000_000,
        40_000_000,
        90_000_000,
        95_000_000,
        142_500_000,
        213_750_000,
        320_625_000,
        480_937_500,
        721_406_250,
        1_082_109_375,
        1_623_164_063,
        2_100_000_000,
        0,
    ];

    public static uint GetNextLevelExperienceRequirement(byte currentLevel)
    {
        if (currentLevel > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(currentLevel), currentLevel, $"Weapon-skill level must be between 0 and {MaximumLevel}.");
        }

        return s_nextLevelExperienceRequirementByCurrentLevel[currentLevel];
    }
}
