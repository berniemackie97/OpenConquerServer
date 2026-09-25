using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Login;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterBootstrapPacketFactoryTests
{
    [Fact]
    public void Accepted_UsesEstablishedCompatibilityPolicy()
    {
        var packet = ExistingCharacterBootstrapPacketFactory.Accepted;

        Assert.Equal((ushort)1004, packet.PacketId);
        Assert.Equal((uint)0, packet.Color);
        Assert.Equal((ushort)0x835, packet.Channel);
        Assert.Equal((ushort)0, packet.Style);
        Assert.Equal((uint)0, packet.Identity);
        Assert.Equal(string.Empty, packet.Sender);
        Assert.Equal(string.Empty, packet.Recipient);
        Assert.Equal(string.Empty, packet.Suffix);
        Assert.Equal("ANSWER_OK", packet.Message);
    }

    [Fact]
    public void LoginHistory_UsesEstablishedCompatibilityPolicy()
    {
        var packet = ExistingCharacterBootstrapPacketFactory.LoginHistory;

        Assert.Equal((ushort)2078, packet.PacketId);
        Assert.Equal((uint)0, packet.LastLoginTimestamp);
        Assert.Equal((byte)0, packet.LocationWarningFlag);
        Assert.Equal(string.Empty, packet.LastLoginLocation);
    }

    [Fact]
    public void ServerState_UsesEstablishedCompatibilityPolicy()
    {
        var packet = ExistingCharacterBootstrapPacketFactory.ServerState;

        Assert.Equal((ushort)2079, packet.PacketId);
        Assert.Equal((uint)0, packet.State);
    }

    [Fact]
    public void CreateUserInfo_MapsPersistedCharacterStateAndCompatibilityPolicy()
    {
        CharacterLoginProfile profile = CreateProfile(rebirthCount: 2, preRebirthLevel: 130, pkPoints: -25, enlightenmentPoints: 1234);

        var packet = ExistingCharacterBootstrapPacketFactory.CreateUserInfo(profile);

        Assert.Equal(profile.Identity.CharacterId, packet.EntityId);
        Assert.Equal((ushort)0, packet.TransformLookSourceId);
        Assert.Equal(profile.Appearance.Composite, packet.PackedAppearance);
        Assert.Equal(profile.Appearance.Hair, packet.HairComposite);
        Assert.Equal(profile.Economy.Silver, packet.Silver);
        Assert.Equal(profile.Economy.ConquerPoints, packet.ConquerPoints);
        Assert.Equal(profile.Progression.Experience, packet.Experience);
        Assert.Equal((uint)0, packet.LegacyDeed);
        Assert.Equal((uint)0, packet.LegacyMedal);
        Assert.Equal((uint)0, packet.LegacyMedalSelect);
        Assert.Equal((uint)0, packet.VirtuePoints);
        Assert.Equal(13_000_000u, packet.EncodedPreRebirthLevel);
        Assert.Equal(profile.Attributes.Strength, packet.Strength);
        Assert.Equal(profile.Attributes.Agility, packet.Agility);
        Assert.Equal(profile.Attributes.Vitality, packet.Vitality);
        Assert.Equal(profile.Attributes.Spirit, packet.Spirit);
        Assert.Equal(profile.Attributes.UnspentPoints, packet.UnspentAttributePoints);
        Assert.Equal(profile.Vitals.Life, packet.CurrentLife);
        Assert.Equal(profile.Vitals.Mana, packet.CurrentMana);
        Assert.Equal(profile.PkPoints, packet.PkPoints);
        Assert.Equal(profile.Progression.Level, packet.Level);
        Assert.Equal(profile.Progression.Profession, packet.CurrentProfession);
        Assert.Equal(profile.Progression.FirstProfession, packet.FirstProfession);
        Assert.Equal(profile.Progression.PreviousProfession, packet.PreviousProfession);
        Assert.Equal((byte)0, packet.LegacyNobility);
        Assert.Equal(profile.Progression.RebirthCount, packet.RebirthCount);
        Assert.Equal((byte)1, packet.LegacyAutoAllot);
        Assert.Equal((uint)0, packet.AuraTierScore);
        Assert.Equal(profile.EnlightenmentPoints, packet.CoachPointsHundredths);
        Assert.Equal((ushort)0, packet.CoachExperienceShareCount);
        Assert.Equal((ushort)0, packet.CoachSessionState);
        Assert.Equal((uint)0, packet.FlowerStatusTier);
        Assert.Equal(profile.TitleId, packet.TitleId);
        Assert.Equal(profile.Economy.BoundConquerPoints, packet.BoundConquerPoints);
        Assert.Equal((byte)0, packet.ActiveSubProfessionId);
        Assert.Equal((ulong)0, packet.PackedSubProfessionPhases);
        Assert.Equal((uint)0, packet.RacePoints);
        Assert.Equal(profile.Identity.Name, packet.PlayerName);
        Assert.Equal("None", packet.SpouseName);
    }

    [Fact]
    public void CreateUserInfo_ZeroPreRebirthLevelEncodesZero()
    {
        CharacterLoginProfile profile = CreateProfile(rebirthCount: 0, preRebirthLevel: 0);

        var packet = ExistingCharacterBootstrapPacketFactory.CreateUserInfo(profile);

        Assert.Equal((uint)0, packet.EncodedPreRebirthLevel);
    }

    [Fact]
    public void CreateUserInfo_RejectsNullProfile()
    {
        Assert.Throws<ArgumentNullException>(() => ExistingCharacterBootstrapPacketFactory.CreateUserInfo(null!));
    }

    private static CharacterLoginProfile CreateProfile(byte rebirthCount = 0, byte preRebirthLevel = 0, short pkPoints = 0, ushort enlightenmentPoints = 0)
    {
        CharacterLoginIdentity identity = new(CharacterIdentityPolicy.FirstPlayerEntityId, accountId: 42, name: "Bernie");
        CharacterAppearance appearance = new(composite: 2011003, hair: 339);
        CharacterProgression progression = new(level: 120, experience: 0x0102030405060708, profession: 60, firstProfession: 10, previousProfession: 20, rebirthCount, preRebirthLevel);
        CharacterAttributes attributes = new(Strength: 101, Agility: 102, Vitality: 103, Spirit: 104, UnspentPoints: 105);
        CharacterVitals vitals = new(Life: 1234, Mana: 567);
        CharacterEconomy economy = new(Silver: 1_234_567, ConquerPoints: 2_345, BoundConquerPoints: 678);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints, titleId: 321, enlightenmentPoints, location);
    }
}
