using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Domain.Tests.Skills;

public sealed class CharacterMagicTests
{
    [Fact]
    public void Create_ValidMagic_ReturnsValidValue()
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        CharacterMagic magic = CharacterMagic.Create(ownerCharacterId, type: 1000, level: 4, experience: 123456);

        Assert.Equal(ownerCharacterId, magic.OwnerCharacterId);
        Assert.Equal((ushort)1000, magic.Type);
        Assert.Equal((ushort)4, magic.Level);
        Assert.Equal(123456u, magic.Experience);
        Assert.True(magic.IsValid);
    }

    [Fact]
    public void Create_ZeroTypeAndLevel_ArePreserved()
    {
        CharacterMagic magic = CharacterMagic.Create(CharacterIdentityPolicy.FirstPlayerEntityId, type: 0, level: 0, experience: 0);

        Assert.Equal((ushort)0, magic.Type);
        Assert.Equal((ushort)0, magic.Level);
        Assert.True(magic.IsValid);
    }

    [Fact]
    public void Create_MaximumWireValues_ArePreserved()
    {
        CharacterMagic magic = CharacterMagic.Create(CharacterIdentityPolicy.FirstPlayerEntityId, ushort.MaxValue, ushort.MaxValue, uint.MaxValue);

        Assert.Equal(ushort.MaxValue, magic.Type);
        Assert.Equal(ushort.MaxValue, magic.Level);
        Assert.Equal(uint.MaxValue, magic.Experience);
        Assert.True(magic.IsValid);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Create_InvalidOwnerCharacterId_ThrowsArgumentOutOfRangeException(uint ownerCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterMagic.Create(ownerCharacterId, type: 1000, level: 1, experience: 0));

        Assert.Equal("ownerCharacterId", exception.ParamName);
    }

    [Fact]
    public void Equality_IncludesOwnerTypeLevelAndExperience()
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterMagic magic = CharacterMagic.Create(ownerCharacterId, 1000, 4, 123);
        CharacterMagic same = CharacterMagic.Create(ownerCharacterId, 1000, 4, 123);

        Assert.Equal(magic, same);
        Assert.NotEqual(magic, CharacterMagic.Create(ownerCharacterId + 1, 1000, 4, 123));
        Assert.NotEqual(magic, CharacterMagic.Create(ownerCharacterId, 1001, 4, 123));
        Assert.NotEqual(magic, CharacterMagic.Create(ownerCharacterId, 1000, 5, 123));
        Assert.NotEqual(magic, CharacterMagic.Create(ownerCharacterId, 1000, 4, 124));
    }

    [Fact]
    public void Default_IsInvalid()
    {
        CharacterMagic magic = default;

        Assert.False(magic.IsValid);
        Assert.Equal(0u, magic.OwnerCharacterId);
        Assert.Equal((ushort)0, magic.Type);
        Assert.Equal((ushort)0, magic.Level);
        Assert.Equal(0u, magic.Experience);
    }
}
