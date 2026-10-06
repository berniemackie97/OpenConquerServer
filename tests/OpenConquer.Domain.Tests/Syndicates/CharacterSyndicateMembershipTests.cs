using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Domain.Tests.Syndicates;

public sealed class CharacterSyndicateMembershipTests
{
    [Fact]
    public void Create_ValidMembership_PreservesCompleteState()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(
            characterId,
            syndicateId: ushort.MaxValue,
            rank: uint.MaxValue,
            proffer: long.MaxValue,
            positionExpirationUnixSeconds: uint.MaxValue,
            joinDateUnixSeconds: uint.MaxValue);

        Assert.Equal(characterId, membership.CharacterId);
        Assert.Equal(ushort.MaxValue, membership.SyndicateId);
        Assert.Equal(uint.MaxValue, membership.Rank);
        Assert.Equal(long.MaxValue, membership.Proffer);
        Assert.Equal(uint.MaxValue, membership.PositionExpirationUnixSeconds);
        Assert.Equal(uint.MaxValue, membership.JoinDateUnixSeconds);
        Assert.True(membership.IsValid);
    }

    [Fact]
    public void Create_ZeroOpaqueValues_ArePreserved()
    {
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(
            CharacterIdentityPolicy.FirstPlayerEntityId,
            syndicateId: 1,
            rank: 0,
            proffer: 0,
            positionExpirationUnixSeconds: 0,
            joinDateUnixSeconds: 0);

        Assert.Equal(0u, membership.Rank);
        Assert.Equal(0L, membership.Proffer);
        Assert.Equal(0u, membership.PositionExpirationUnixSeconds);
        Assert.Equal(0u, membership.JoinDateUnixSeconds);
        Assert.True(membership.IsValid);
    }

    [Fact]
    public void Create_NegativeProffer_IsPreserved()
    {
        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(
            CharacterIdentityPolicy.FirstPlayerEntityId,
            syndicateId: 1,
            rank: 200,
            proffer: long.MinValue,
            positionExpirationUnixSeconds: 0,
            joinDateUnixSeconds: 0);

        Assert.Equal(long.MinValue, membership.Proffer);
        Assert.True(membership.IsValid);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Create_InvalidCharacterId_ThrowsArgumentOutOfRangeException(uint characterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterSyndicateMembership.Create(
                characterId,
                syndicateId: 1,
                rank: 200,
                proffer: 0,
                positionExpirationUnixSeconds: 0,
                joinDateUnixSeconds: 0));

        Assert.Equal("characterId", exception.ParamName);
    }

    [Fact]
    public void Create_ZeroSyndicateId_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterSyndicateMembership.Create(
                CharacterIdentityPolicy.FirstPlayerEntityId,
                syndicateId: 0,
                rank: 200,
                proffer: 0,
                positionExpirationUnixSeconds: 0,
                joinDateUnixSeconds: 0));

        Assert.Equal("syndicateId", exception.ParamName);
    }

    [Fact]
    public void Equality_IncludesAllPersistedMembershipState()
    {
        uint characterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(characterId, 1, 200, 123, 456, 789);
        CharacterSyndicateMembership same = CharacterSyndicateMembership.Create(characterId, 1, 200, 123, 456, 789);

        Assert.Equal(membership, same);
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId + 1, 1, 200, 123, 456, 789));
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId, 2, 200, 123, 456, 789));
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId, 1, 201, 123, 456, 789));
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId, 1, 200, 124, 456, 789));
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId, 1, 200, 123, 457, 789));
        Assert.NotEqual(membership, CharacterSyndicateMembership.Create(characterId, 1, 200, 123, 456, 790));
    }

    [Fact]
    public void Default_IsInvalid()
    {
        CharacterSyndicateMembership membership = default;

        Assert.False(membership.IsValid);
        Assert.Equal(0u, membership.CharacterId);
        Assert.Equal((ushort)0, membership.SyndicateId);
        Assert.Equal(0u, membership.Rank);
        Assert.Equal(0L, membership.Proffer);
        Assert.Equal(0u, membership.PositionExpirationUnixSeconds);
        Assert.Equal(0u, membership.JoinDateUnixSeconds);
    }
}
