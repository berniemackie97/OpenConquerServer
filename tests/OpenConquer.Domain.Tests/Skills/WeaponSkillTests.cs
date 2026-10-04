using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Domain.Tests.Skills;

public sealed class WeaponSkillTests
{
    [Theory]
    [InlineData((byte)0, 0u)]
    [InlineData((byte)1, 1_200u)]
    [InlineData((byte)19, 2_100_000_000u)]
    [InlineData((byte)20, 0u)]
    public void Create_ValidSkill_ReturnsValidValue(byte level, uint expectedNextLevelExperienceRequirement)
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        WeaponSkill skill = WeaponSkill.Create(ownerCharacterId, type: 410, level, experience: 123456);

        Assert.Equal(ownerCharacterId, skill.OwnerCharacterId);
        Assert.Equal(410u, skill.Type);
        Assert.Equal(level, skill.Level);
        Assert.Equal(123456u, skill.Experience);
        Assert.Equal(expectedNextLevelExperienceRequirement, skill.NextLevelExperienceRequirement);
        Assert.True(skill.IsValid);
    }

    [Fact]
    public void Create_FourDigitType_PreservesTypeUnmodified()
    {
        WeaponSkill skill = WeaponSkill.Create(CharacterIdentityPolicy.FirstPlayerEntityId, type: 1050, level: 1, experience: 0);

        Assert.Equal(1050u, skill.Type);
        Assert.True(skill.IsValid);
    }

    [Fact]
    public void Create_ExperienceAtOrAboveNextRequirement_IsAllowed()
    {
        WeaponSkill atRequirement = WeaponSkill.Create(CharacterIdentityPolicy.FirstPlayerEntityId, type: 410, level: 1, experience: 1_200);
        WeaponSkill aboveRequirement = WeaponSkill.Create(CharacterIdentityPolicy.FirstPlayerEntityId, type: 410, level: 1, experience: uint.MaxValue);

        Assert.True(atRequirement.IsValid);
        Assert.True(aboveRequirement.IsValid);
        Assert.Equal(1_200u, atRequirement.Experience);
        Assert.Equal(uint.MaxValue, aboveRequirement.Experience);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Create_InvalidOwnerCharacterId_ThrowsArgumentOutOfRangeException(uint ownerCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeaponSkill.Create(ownerCharacterId, type: 410, level: 1, experience: 0));

        Assert.Equal("ownerCharacterId", exception.ParamName);
    }

    [Theory]
    [InlineData((byte)21)]
    [InlineData(byte.MaxValue)]
    public void Create_LevelAboveMaximum_ThrowsArgumentOutOfRangeException(byte level)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeaponSkill.Create(CharacterIdentityPolicy.FirstPlayerEntityId, type: 410, level, experience: 0));

        Assert.Equal("level", exception.ParamName);
    }

    [Fact]
    public void Equality_IncludesOwnerTypeLevelAndExperience()
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        WeaponSkill skill = WeaponSkill.Create(ownerCharacterId, 410, 5, 123);
        WeaponSkill same = WeaponSkill.Create(ownerCharacterId, 410, 5, 123);

        Assert.Equal(skill, same);
        Assert.NotEqual(skill, WeaponSkill.Create(ownerCharacterId + 1, 410, 5, 123));
        Assert.NotEqual(skill, WeaponSkill.Create(ownerCharacterId, 420, 5, 123));
        Assert.NotEqual(skill, WeaponSkill.Create(ownerCharacterId, 410, 6, 123));
        Assert.NotEqual(skill, WeaponSkill.Create(ownerCharacterId, 410, 5, 124));
    }

    [Fact]
    public void Default_IsInvalid()
    {
        WeaponSkill skill = default;

        Assert.False(skill.IsValid);
        Assert.Equal(0u, skill.OwnerCharacterId);
        Assert.Equal(0u, skill.Type);
        Assert.Equal((byte)0, skill.Level);
        Assert.Equal(0u, skill.Experience);
        Assert.Equal(0u, skill.NextLevelExperienceRequirement);
    }
}
