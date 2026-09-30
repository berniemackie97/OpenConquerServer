using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Domain.Tests.Social;

public sealed class SocialRelationTests
{
    [Theory]
    [InlineData(SocialRelationKind.Friend)]
    [InlineData(SocialRelationKind.Enemy)]
    public void Create_ValidRelation_ReturnsValidValue(SocialRelationKind kind)
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        uint counterpartCharacterId = ownerCharacterId + 1;

        SocialRelation relation = SocialRelation.Create(ownerCharacterId, counterpartCharacterId, kind);

        Assert.Equal(ownerCharacterId, relation.OwnerCharacterId);
        Assert.Equal(counterpartCharacterId, relation.CounterpartCharacterId);
        Assert.Equal(kind, relation.Kind);
        Assert.True(relation.IsValid);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Create_InvalidOwnerCharacterId_ThrowsArgumentOutOfRangeException(uint ownerCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SocialRelation.Create(ownerCharacterId, CharacterIdentityPolicy.FirstPlayerEntityId, SocialRelationKind.Friend));

        Assert.Equal("ownerCharacterId", exception.ParamName);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Create_InvalidCounterpartCharacterId_ThrowsArgumentOutOfRangeException(uint counterpartCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SocialRelation.Create(CharacterIdentityPolicy.FirstPlayerEntityId, counterpartCharacterId, SocialRelationKind.Friend));

        Assert.Equal("counterpartCharacterId", exception.ParamName);
    }

    [Theory]
    [InlineData(SocialRelationKind.Friend)]
    [InlineData(SocialRelationKind.Enemy)]
    public void Create_SelfRelation_ThrowsArgumentException(SocialRelationKind kind)
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SocialRelation.Create(characterId, characterId, kind));

        Assert.Equal("counterpartCharacterId", exception.ParamName);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)3)]
    [InlineData(byte.MaxValue)]
    public void Create_UndefinedKind_ThrowsArgumentOutOfRangeException(byte value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SocialRelation.Create(CharacterIdentityPolicy.FirstPlayerEntityId, CharacterIdentityPolicy.FirstPlayerEntityId + 1, (SocialRelationKind)value));

        Assert.Equal("kind", exception.ParamName);
    }

    [Fact]
    public void Equality_IncludesDirectionAndKind()
    {
        uint first = CharacterIdentityPolicy.FirstPlayerEntityId;
        uint second = first + 1;

        SocialRelation friend = SocialRelation.Create(first, second, SocialRelationKind.Friend);
        SocialRelation sameFriend = SocialRelation.Create(first, second, SocialRelationKind.Friend);
        SocialRelation reversedFriend = SocialRelation.Create(second, first, SocialRelationKind.Friend);
        SocialRelation enemy = SocialRelation.Create(first, second, SocialRelationKind.Enemy);

        Assert.Equal(friend, sameFriend);
        Assert.NotEqual(friend, reversedFriend);
        Assert.NotEqual(friend, enemy);
    }

    [Fact]
    public void Default_IsInvalid()
    {
        SocialRelation relation = default;

        Assert.False(relation.IsValid);
    }
}
