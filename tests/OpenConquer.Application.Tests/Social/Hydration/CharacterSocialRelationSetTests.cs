using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Application.Tests.Social.Hydration;

public sealed class CharacterSocialRelationSetTests
{
    [Fact]
    public void Constructor_EmptyRelationSet_IsAccepted()
    {
        CharacterSocialRelationSet relationSet = new(CharacterIdentityPolicy.FirstPlayerEntityId, []);

        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, relationSet.CharacterId);
        Assert.Empty(relationSet.Relations);
        Assert.Equal(0, relationSet.Count);
    }

    [Fact]
    public void Constructor_UnorderedRelations_SortsByKindThenCounterpartCharacterId()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSocialRelation[] relations =
        [
            CreateRelation(characterId, characterId + 3, SocialRelationKind.Enemy, "EnemyC"),
            CreateRelation(characterId, characterId + 2, SocialRelationKind.Friend, "FriendB"),
            CreateRelation(characterId, characterId + 1, SocialRelationKind.Friend, "FriendA"),
            CreateRelation(characterId, characterId + 4, SocialRelationKind.Enemy, "EnemyD"),
        ];

        CharacterSocialRelationSet relationSet = new(characterId, relations);

        Assert.Equal(
        [
            (SocialRelationKind.Friend, characterId + 1),
            (SocialRelationKind.Friend, characterId + 2),
            (SocialRelationKind.Enemy, characterId + 3),
            (SocialRelationKind.Enemy, characterId + 4),
        ], relationSet.Relations.Select(static relation => (relation.Kind, relation.CounterpartCharacterId)));
    }

    [Fact]
    public void Constructor_CapturesRelationsIndependentlyOfSourceCollection()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        List<CharacterSocialRelation> relations = [CreateRelation(characterId, characterId + 1, SocialRelationKind.Friend, "Bernie")];

        CharacterSocialRelationSet relationSet = new(characterId, relations);
        relations.Clear();

        Assert.Single(relationSet.Relations);
        Assert.Equal(characterId + 1, relationSet.Relations[0].CounterpartCharacterId);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_NonPlayerCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterSocialRelationSet(characterId, []));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullRelations_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new CharacterSocialRelationSet(CharacterIdentityPolicy.FirstPlayerEntityId, null!));

        Assert.Equal("relations", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullRelation_ThrowsArgumentException()
    {
        CharacterSocialRelation[] relations = [null!];

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSocialRelationSet(CharacterIdentityPolicy.FirstPlayerEntityId, relations));

        Assert.Equal("relations", exception.ParamName);
    }

    [Fact]
    public void Constructor_RelationOwnedByDifferentCharacter_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSocialRelation relation = CreateRelation(characterId + 1, characterId + 2, SocialRelationKind.Friend, "Bernie");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSocialRelationSet(characterId, [relation]));

        Assert.Equal("relations", exception.ParamName);
    }

    [Theory]
    [InlineData(SocialRelationKind.Friend)]
    [InlineData(SocialRelationKind.Enemy)]
    public void Constructor_DuplicateRelation_ThrowsArgumentException(SocialRelationKind kind)
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        uint counterpartCharacterId = characterId + 1;
        CharacterSocialRelation first = CreateRelation(characterId, counterpartCharacterId, kind, "Bernie");
        CharacterSocialRelation second = CreateRelation(characterId, counterpartCharacterId, kind, "Bernie");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSocialRelationSet(characterId, [first, second]));

        Assert.Equal("relations", exception.ParamName);
    }

    [Fact]
    public void Constructor_SameCounterpartWithDifferentKinds_IsAccepted()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        uint counterpartCharacterId = characterId + 1;
        CharacterSocialRelation friend = CreateRelation(characterId, counterpartCharacterId, SocialRelationKind.Friend, "Bernie");
        CharacterSocialRelation enemy = CreateRelation(characterId, counterpartCharacterId, SocialRelationKind.Enemy, "Bernie");

        CharacterSocialRelationSet relationSet = new(characterId, [enemy, friend]);

        Assert.Equal(2, relationSet.Count);
        Assert.Equal(SocialRelationKind.Friend, relationSet.Relations[0].Kind);
        Assert.Equal(SocialRelationKind.Enemy, relationSet.Relations[1].Kind);
        Assert.All(relationSet.Relations, relation => Assert.Equal(counterpartCharacterId, relation.CounterpartCharacterId));
    }

    private static CharacterSocialRelation CreateRelation(uint ownerCharacterId, uint counterpartCharacterId, SocialRelationKind kind, string counterpartName)
    {
        SocialRelation relation = SocialRelation.Create(ownerCharacterId, counterpartCharacterId, kind);
        return new CharacterSocialRelation(relation, counterpartName);
    }
}
