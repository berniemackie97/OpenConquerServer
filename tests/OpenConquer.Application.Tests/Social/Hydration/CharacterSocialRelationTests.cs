using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Application.Tests.Social.Hydration;

public sealed class CharacterSocialRelationTests
{
    [Theory]
    [InlineData(SocialRelationKind.Friend)]
    [InlineData(SocialRelationKind.Enemy)]
    public void Constructor_ValidRelation_PreservesHydratedState(SocialRelationKind kind)
    {
        uint ownerCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        uint counterpartCharacterId = ownerCharacterId + 1;
        SocialRelation relation = SocialRelation.Create(ownerCharacterId, counterpartCharacterId, kind);

        CharacterSocialRelation hydrated = new(relation, "Bernie");

        Assert.Equal(relation, hydrated.Relation);
        Assert.Equal("Bernie", hydrated.CounterpartName);
        Assert.Equal(ownerCharacterId, hydrated.OwnerCharacterId);
        Assert.Equal(counterpartCharacterId, hydrated.CounterpartCharacterId);
        Assert.Equal(kind, hydrated.Kind);
    }

    [Fact]
    public void Constructor_InvalidRelation_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterSocialRelation(default, "Bernie"));

        Assert.Equal("relation", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    [InlineData("Bad Name")]
    [InlineData("Bad/Name")]
    [InlineData("Bad[Name")]
    [InlineData("漢字名前")]
    public void Constructor_InvalidCounterpartName_ThrowsArgumentException(string counterpartName)
    {
        SocialRelation relation = CreateRelation();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterSocialRelation(relation, counterpartName));

        Assert.Equal("counterpartName", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullCounterpartName_ThrowsArgumentException()
    {
        SocialRelation relation = CreateRelation();

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new CharacterSocialRelation(relation, null!));

        Assert.Equal("counterpartName", exception.ParamName);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("ABCDEFGHIJKLMNO")]
    [InlineData("€ABC")]
    public void Constructor_ValidBoundaryName_IsAccepted(string counterpartName)
    {
        SocialRelation relation = CreateRelation();

        CharacterSocialRelation hydrated = new(relation, counterpartName);

        Assert.Equal(counterpartName, hydrated.CounterpartName);
    }

    private static SocialRelation CreateRelation()
    {
        return SocialRelation.Create(CharacterIdentityPolicy.FirstPlayerEntityId, CharacterIdentityPolicy.FirstPlayerEntityId + 1, SocialRelationKind.Friend);
    }
}
