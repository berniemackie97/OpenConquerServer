using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Application.Tests.Skills.Hydration;

public sealed class CharacterWeaponSkillSetTests
{
    [Fact]
    public void Constructor_EmptySkillSet_IsAccepted()
    {
        CharacterWeaponSkillSet skillSet = new(CharacterIdentityPolicy.FirstPlayerEntityId, []);

        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, skillSet.CharacterId);
        Assert.Empty(skillSet.Skills);
        Assert.Equal(0, skillSet.Count);
    }

    [Fact]
    public void Constructor_UnorderedSkills_SortsByType()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        WeaponSkill[] skills =
        [
            CreateSkill(characterId, 1050),
            CreateSkill(characterId, 420),
            CreateSkill(characterId, 410),
            CreateSkill(characterId, 500),
        ];

        CharacterWeaponSkillSet skillSet = new(characterId, skills);

        Assert.Equal([410u, 420u, 500u, 1050u], skillSet.Skills.Select(static skill => skill.Type));
    }

    [Fact]
    public void Constructor_CapturesSkillsIndependentlyOfSourceCollection()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        List<WeaponSkill> skills = [CreateSkill(characterId, 410)];

        CharacterWeaponSkillSet skillSet = new(characterId, skills);
        skills.Clear();

        Assert.Single(skillSet.Skills);
        Assert.Equal(410u, skillSet.Skills[0].Type);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_NonPlayerCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterWeaponSkillSet(characterId, []));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullSkills_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new CharacterWeaponSkillSet(CharacterIdentityPolicy.FirstPlayerEntityId, null!));

        Assert.Equal("skills", exception.ParamName);
    }

    [Fact]
    public void Constructor_InvalidSkill_ThrowsArgumentException()
    {
        WeaponSkill[] skills = [default];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterWeaponSkillSet(CharacterIdentityPolicy.FirstPlayerEntityId, skills));

        Assert.Equal("skills", exception.ParamName);
    }

    [Fact]
    public void Constructor_SkillOwnedByDifferentCharacter_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        WeaponSkill skill = CreateSkill(characterId + 1, 410);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterWeaponSkillSet(characterId, [skill]));

        Assert.Equal("skills", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateType_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        WeaponSkill first = CreateSkill(characterId, 410, level: 1, experience: 100);
        WeaponSkill second = CreateSkill(characterId, 410, level: 5, experience: 200);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterWeaponSkillSet(characterId, [first, second]));

        Assert.Equal("skills", exception.ParamName);
    }

    [Fact]
    public void Constructor_DifferentTypes_AreAccepted()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterWeaponSkillSet skillSet = new(characterId,
        [
            CreateSkill(characterId, 410),
            CreateSkill(characterId, 420),
        ]);

        Assert.Equal(2, skillSet.Count);
        Assert.Equal(410u, skillSet.Skills[0].Type);
        Assert.Equal(420u, skillSet.Skills[1].Type);
    }

    private static WeaponSkill CreateSkill(uint ownerCharacterId, uint type, byte level = 1, uint experience = 0)
    {
        return WeaponSkill.Create(ownerCharacterId, type, level, experience);
    }
}
