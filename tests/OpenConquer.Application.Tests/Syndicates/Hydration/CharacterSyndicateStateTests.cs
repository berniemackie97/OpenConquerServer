using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Application.Tests.Syndicates.Hydration;

public sealed class CharacterSyndicateStateTests
{
    [Fact]
    public void Constructor_NoMembership_CreatesEmptyState()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        CharacterSyndicateState state = new(characterId, membership: null, syndicate: null);

        Assert.Equal(characterId, state.CharacterId);
        Assert.Null(state.Membership);
        Assert.Null(state.Syndicate);
        Assert.False(state.HasMembership);
    }

    [Fact]
    public void Constructor_ValidMembership_PreservesHydratedState()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(
            characterId,
            syndicateId: 123,
            rank: 200,
            proffer: 456,
            positionExpirationUnixSeconds: 789,
            joinDateUnixSeconds: 101112);

        Syndicate syndicate = CreateSyndicate(123);

        CharacterSyndicateState state = new(characterId, membership, syndicate);

        Assert.Equal(characterId, state.CharacterId);
        Assert.Equal(membership, state.Membership);
        Assert.Same(syndicate, state.Syndicate);
        Assert.True(state.HasMembership);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_InvalidCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterSyndicateState(characterId, membership: null, syndicate: null));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_MembershipWithoutSyndicate_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(characterId, 123, 200, 0, 0, 0);

        Assert.Throws<ArgumentException>(() =>
            new CharacterSyndicateState(characterId, membership, syndicate: null));
    }

    [Fact]
    public void Constructor_SyndicateWithoutMembership_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        Assert.Throws<ArgumentException>(() =>
            new CharacterSyndicateState(characterId, membership: null, CreateSyndicate(123)));
    }

    [Fact]
    public void Constructor_InvalidMembership_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSyndicateMembership membership = default;

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSyndicateState(characterId, membership, CreateSyndicate(123)));

        Assert.Equal("membership", exception.ParamName);
    }

    [Fact]
    public void Constructor_MembershipForDifferentCharacter_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(characterId + 1, 123, 200, 0, 0, 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSyndicateState(characterId, membership, CreateSyndicate(123)));

        Assert.Equal("membership", exception.ParamName);
    }

    [Fact]
    public void Constructor_MismatchedSyndicate_ThrowsArgumentException()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(characterId, 123, 200, 0, 0, 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new CharacterSyndicateState(characterId, membership, CreateSyndicate(124)));

        Assert.Equal("syndicate", exception.ParamName);
    }

    private static Syndicate CreateSyndicate(ushort syndicateId)
    {
        return new Syndicate(
            syndicateId,
            name: "OpenConquer",
            leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
            leaderName: "Bernie",
            silverFund: 123,
            emoneyFund: 456,
            population: 10,
            requiredLevel: 0,
            requiredProfession: 0,
            requiredMetempsychosis: 0);
    }
}
