using OpenConquer.Infrastructure.Persistence.Game.Skills;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class CharacterWeaponSkillHydrationOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        CharacterWeaponSkillHydrationOptions options = new();

        Assert.Equal(CharacterWeaponSkillHydrationOptions.DefaultMaximumSkillsPerCharacter, options.MaximumSkillsPerCharacter);
        Assert.Equal(4_096, options.MaximumSkillsPerCharacter);
    }

    [Fact]
    public void Constructor_CustomValueIsPreserved()
    {
        CharacterWeaponSkillHydrationOptions options = new(maximumSkillsPerCharacter: 512);

        Assert.Equal(512, options.MaximumSkillsPerCharacter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CharacterWeaponSkillHydrationOptions.MaximumSupportedSkillsPerCharacter + 1)]
    public void Constructor_InvalidLimitThrows(int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterWeaponSkillHydrationOptions(value));

        Assert.Equal("maximumSkillsPerCharacter", exception.ParamName);
    }

    [Fact]
    public void Constructor_MaximumSupportedLimitIsAccepted()
    {
        CharacterWeaponSkillHydrationOptions options = new(CharacterWeaponSkillHydrationOptions.MaximumSupportedSkillsPerCharacter);

        Assert.Equal(CharacterWeaponSkillHydrationOptions.MaximumSupportedSkillsPerCharacter, options.MaximumSkillsPerCharacter);
    }
}
