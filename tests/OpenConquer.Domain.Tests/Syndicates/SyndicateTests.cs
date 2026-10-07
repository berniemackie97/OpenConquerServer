using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Domain.Tests.Syndicates;

public sealed class SyndicateTests
{
    [Fact]
    public void Constructor_ValidState_PreservesCompleteBootstrapDomainState()
    {
        uint leaderCharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;

        Syndicate syndicate = new(
            syndicateId: ushort.MaxValue,
            name: "OpenConquer",
            leaderCharacterId,
            leaderName: "Bernie",
            silverFund: ulong.MaxValue,
            emoneyFund: uint.MaxValue,
            population: uint.MaxValue,
            requiredLevel: byte.MaxValue,
            requiredProfession: byte.MaxValue,
            requiredMetempsychosis: byte.MaxValue);

        Assert.Equal(ushort.MaxValue, syndicate.SyndicateId);
        Assert.Equal("OpenConquer", syndicate.Name);
        Assert.Equal(leaderCharacterId, syndicate.LeaderCharacterId);
        Assert.Equal("Bernie", syndicate.LeaderName);
        Assert.Equal(ulong.MaxValue, syndicate.SilverFund);
        Assert.Equal(uint.MaxValue, syndicate.EmoneyFund);
        Assert.Equal(uint.MaxValue, syndicate.Population);
        Assert.Equal(byte.MaxValue, syndicate.RequiredLevel);
        Assert.Equal(byte.MaxValue, syndicate.RequiredProfession);
        Assert.Equal(byte.MaxValue, syndicate.RequiredMetempsychosis);
    }

    [Fact]
    public void Constructor_ZeroRequirementAndPopulationValues_ArePreserved()
    {
        Syndicate syndicate = new(
            syndicateId: 1,
            name: "X",
            leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
            leaderName: "Bernie",
            silverFund: 0,
            emoneyFund: 0,
            population: 0,
            requiredLevel: 0,
            requiredProfession: 0,
            requiredMetempsychosis: 0);

        Assert.Equal("X", syndicate.Name);
        Assert.Equal(0ul, syndicate.SilverFund);
        Assert.Equal(0u, syndicate.EmoneyFund);
        Assert.Equal(0u, syndicate.Population);
        Assert.Equal((byte)0, syndicate.RequiredLevel);
        Assert.Equal((byte)0, syndicate.RequiredProfession);
        Assert.Equal((byte)0, syndicate.RequiredMetempsychosis);
    }

    [Fact]
    public void Constructor_ZeroSyndicateId_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Syndicate(
                syndicateId: 0,
                name: "OpenConquer",
                leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
                leaderName: "Bernie",
                silverFund: 0,
                emoneyFund: 0,
                population: 1,
                requiredLevel: 1,
                requiredProfession: 0,
                requiredMetempsychosis: 0));

        Assert.Equal("syndicateId", exception.ParamName);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(CharacterIdentityPolicy.FirstPlayerEntityId - 1)]
    public void Constructor_InvalidLeaderCharacterId_ThrowsArgumentOutOfRangeException(uint leaderCharacterId)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Syndicate(
                syndicateId: 1,
                name: "OpenConquer",
                leaderCharacterId,
                leaderName: "Bernie",
                silverFund: 0,
                emoneyFund: 0,
                population: 1,
                requiredLevel: 1,
                requiredProfession: 0,
                requiredMetempsychosis: 0));

        Assert.Equal("leaderCharacterId", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullName_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new Syndicate(
                syndicateId: 1,
                name: null!,
                leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
                leaderName: "Bernie",
                silverFund: 0,
                emoneyFund: 0,
                population: 1,
                requiredLevel: 1,
                requiredProfession: 0,
                requiredMetempsychosis: 0));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678901234567")]
    [InlineData("Open漢Conquer")]
    public void Constructor_InvalidName_ThrowsArgumentException(string name)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new Syndicate(
                syndicateId: 1,
                name,
                leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
                leaderName: "Bernie",
                silverFund: 0,
                emoneyFund: 0,
                population: 1,
                requiredLevel: 1,
                requiredProfession: 0,
                requiredMetempsychosis: 0));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("Bad Name")]
    public void Constructor_InvalidLeaderName_ThrowsArgumentException(string? leaderName)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Syndicate(
                syndicateId: 1,
                name: "OpenConquer",
                leaderCharacterId: CharacterIdentityPolicy.FirstPlayerEntityId,
                leaderName: leaderName!,
                silverFund: 0,
                emoneyFund: 0,
                population: 1,
                requiredLevel: 1,
                requiredProfession: 0,
                requiredMetempsychosis: 0));

        Assert.Equal("leaderName", exception.ParamName);
    }
}
