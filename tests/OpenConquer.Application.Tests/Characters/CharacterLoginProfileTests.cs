using OpenConquer.Application.Characters.Login;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters;

public sealed class CharacterLoginProfileTests
{
    [Fact]
    public void Constructor_PreservesCompleteLoginSnapshot()
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 120, experience: 123_456_789, profession: 15, firstProfession: 10, previousProfession: 15, rebirthCount: 1);
        CharacterAttributes attributes = new(120, 65, 80, 20, 7);
        CharacterVitals vitals = new(2_500, 600);
        CharacterEconomy economy = new(500_000, 12_345, 678);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        CharacterLoginProfile profile = new(identity, appearance, progression, attributes, vitals, economy,
            pkPoints: 25, titleId: 7, enlightenmentPoints: 250, location);

        Assert.Same(identity, profile.Identity);
        Assert.Same(appearance, profile.Appearance);
        Assert.Same(progression, profile.Progression);
        Assert.Equal(attributes, profile.Attributes);
        Assert.Equal(vitals, profile.Vitals);
        Assert.Equal(economy, profile.Economy);
        Assert.Equal((ushort)25, profile.PkPoints);
        Assert.Equal((ushort)7, profile.TitleId);
        Assert.Equal((ushort)250, profile.EnlightenmentPoints);
        Assert.Same(location, profile.Location);
    }

    [Fact]
    public void Constructor_NullRequiredComponentIsRejected()
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 42, "Bernie");
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        Assert.Throws<ArgumentNullException>(() => new CharacterLoginProfile(null!, appearance, progression, default, default, default, 0, 0, 0, location));
        Assert.Throws<ArgumentNullException>(() => new CharacterLoginProfile(identity, null!, progression, default, default, default, 0, 0, 0, location));
        Assert.Throws<ArgumentNullException>(() => new CharacterLoginProfile(identity, appearance, null!, default, default, default, 0, 0, 0, location));
        Assert.Throws<ArgumentNullException>(() => new CharacterLoginProfile(identity, appearance, progression, default, default, default, 0, 0, 0, null!));
    }

    [Fact]
    public void ExistingCharacterResolution_PreservesProfile()
    {
        CharacterLoginProfile profile = CreateProfile();

        CharacterLoginResolution resolution = CharacterLoginResolution.ExistingCharacter(profile);

        Assert.Equal(CharacterLoginRoute.ExistingCharacter, resolution.Route);
        Assert.Same(profile, resolution.Profile);
    }

    [Fact]
    public void ExistingCharacterResolution_NullProfileIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => CharacterLoginResolution.ExistingCharacter(null!));
    }

    private static CharacterLoginProfile CreateProfile()
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 42, "Bernie"),
            new CharacterAppearance(composite: 1003, hair: 410),
            new CharacterProgression(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0),
            new CharacterAttributes(10, 10, 10, 10, 0),
            new CharacterVitals(100, 0),
            new CharacterEconomy(0, 0, 0),
            pkPoints: 0,
            titleId: 0,
            enlightenmentPoints: 0,
            new CharacterLocation(mapId: 1002, x: 430, y: 378));
    }
}
