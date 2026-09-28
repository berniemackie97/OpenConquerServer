using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters;

public sealed class CharacterLoginContractTests
{
    [Fact]
    public void CharacterIdentityPolicy_PlayerBoundaryIsExact()
    {
        Assert.False(CharacterIdentityPolicy.IsPlayerEntityId(CharacterIdentityPolicy.FirstPlayerEntityId - 1));
        Assert.True(CharacterIdentityPolicy.IsPlayerEntityId(CharacterIdentityPolicy.FirstPlayerEntityId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(140)]
    public void CharacterProgression_ValidBoundaryLevelsAreAccepted(byte level)
    {
        CharacterProgression progression = new(level, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);

        Assert.Equal(level, progression.Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(141)]
    public void CharacterProgression_InvalidLevelIsRejected(byte level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterProgression(level, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0));
    }

    [Fact]
    public void CharacterProgression_ZeroProfessionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterProgression(level: 1, experience: 0, profession: 0, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0));
    }

    [Fact]
    public void CharacterLoginIdentity_InvalidPersistedIdentityIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId - 1, accountId: 1, "Hero"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 0, "Hero"));

        Assert.Throws<ArgumentException>(() =>
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 1, "abc"));
    }

    [Fact]
    public void CharacterAppearance_ZeroCompositeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterAppearance(composite: 0, hair: 0));
    }

    [Fact]
    public void CharacterLocation_ZeroMapIdIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLocation(mapId: 0, x: 0, y: 0));
    }

    [Fact]
    public void DefaultResolution_IsUnspecified()
    {
        CharacterLoginResolution resolution = default;

        Assert.Equal(CharacterLoginRoute.Unspecified, resolution.Route);
        Assert.Null(resolution.Profile);
    }

    [Fact]
    public void CharacterCreationResolution_HasNoProfile()
    {
        CharacterLoginResolution resolution = CharacterLoginResolution.CharacterCreation();

        Assert.Equal(CharacterLoginRoute.CharacterCreation, resolution.Route);
        Assert.Null(resolution.Profile);
    }
}
