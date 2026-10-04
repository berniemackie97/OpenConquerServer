using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Application.Tests.Skills.Hydration;

public sealed class CharacterMagicSetTests
{
    [Fact]
    public void Constructor_EmptyMagicSet_IsAccepted()
    {
        CharacterMagicSet magicSet = new(CharacterIdentityPolicy.FirstPlayerEntityId, []);

        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, magicSet.CharacterId);
        Assert.Empty(magicSet.Magic);
        Assert.Equal(0, magicSet.Count);
    }

    [Fact]
    public void Constructor_UnorderedMagic_SortsByType()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterMagic[] magic =
        [
            CreateMagic(characterId, 1300),
            CreateMagic(characterId, 1000),
            CreateMagic(characterId, 1200),
            CreateMagic(characterId, 1100),
        ];

        CharacterMagicSet magicSet = new(characterId, magic);

        Assert.Equal([(ushort)1000, (ushort)1100, (ushort)1200, (ushort)1300], magicSet.Magic.Select(static entry => entry.Type));
    }

    [Fact]
    public void Constructor_CapturesMagicIndependentlyOfSourceCollection()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        List<CharacterMagic> magic = [CreateMagic(characterId, 1000)];

        CharacterMagicSet magicSet = new(characterId, magic);
        magic.Clear();

        Assert.Single(magicSet.Magic);
        Assert.Equal((ushort)1000, magicSet.Magic[0].Type);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_NonPlayerCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterMagicSet(characterId, []));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullMagic_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new CharacterMagicSet(CharacterIdentityPolicy.FirstPlayerEntityId, null!));

        Assert.Equal("magic", exception.ParamName);
    }

    [Fact]
    public void Constructor_InvalidMagic_ThrowsArgumentException()
    {
        CharacterMagic[] magic = [default];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterMagicSet(CharacterIdentityPolicy.FirstPlayerEntityId, magic));

        Assert.Equal("magic", exception.ParamName);
    }

    [Fact]
    public void Constructor_MagicOwnedByDifferentCharacter_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterMagic magic = CreateMagic(characterId + 1, 1000);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterMagicSet(characterId, [magic]));

        Assert.Equal("magic", exception.ParamName);
    }

    [Fact]
    public void Constructor_DuplicateType_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterMagic first = CreateMagic(characterId, 1000, level: 1, experience: 100);
        CharacterMagic second = CreateMagic(characterId, 1000, level: 5, experience: 200);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterMagicSet(characterId, [first, second]));

        Assert.Equal("magic", exception.ParamName);
    }

    [Fact]
    public void Constructor_DifferentTypes_AreAccepted()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterMagicSet magicSet = new(characterId,
        [
            CreateMagic(characterId, 1000),
            CreateMagic(characterId, 1100),
        ]);

        Assert.Equal(2, magicSet.Count);
        Assert.Equal((ushort)1000, magicSet.Magic[0].Type);
        Assert.Equal((ushort)1100, magicSet.Magic[1].Type);
    }

    private static CharacterMagic CreateMagic(uint ownerCharacterId, ushort type, ushort level = 1, uint experience = 0)
    {
        return CharacterMagic.Create(ownerCharacterId, type, level, experience);
    }
}
