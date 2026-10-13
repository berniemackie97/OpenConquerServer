using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Bootstrap;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.World;

namespace OpenConquer.GameServer.Tests.Login.Bootstrap;

public sealed class ExistingCharacterLoginContextTests
{
    private const uint CharacterId = LoginStepHarness.CharacterId;
    private const uint OtherCharacterId = CharacterId + 1;

    [Fact]
    public async Task Constructor_MapDoesNotMatchPersistedCharacter_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        await using ExistingCharacterGameConnection connection = new(harness.Connection, LoginStepHarness.CreateProfile(),
            harness.Presence.Register(CharacterId));
        WorldMapContent otherMap = WorldMapTestContent.Create(LoginStepHarness.MapId + 1, LoginStepHarness.MapDataId, LoginStepHarness.MapFlags);

        Assert.Throws<ArgumentException>(() => new ExistingCharacterLoginContext(connection, otherMap));
    }

    [Fact]
    public async Task StepState_FromAnotherCharacter_IsRejected()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        Assert.Throws<ArgumentException>(() => login.ItemSet = new CharacterItemSet(OtherCharacterId, []));
        Assert.Throws<ArgumentException>(() => login.SocialRelationSet = new CharacterSocialRelationSet(OtherCharacterId, []));
        Assert.Throws<ArgumentException>(() => login.WeaponSkillSet = new CharacterWeaponSkillSet(OtherCharacterId, []));
        Assert.Throws<ArgumentException>(() => login.MagicSet = new CharacterMagicSet(OtherCharacterId, []));
        Assert.Throws<ArgumentException>(() => login.SyndicateState = new CharacterSyndicateState(OtherCharacterId, membership: null, syndicate: null));

        Assert.Null(login.ItemSet);
        Assert.Null(login.SocialRelationSet);
        Assert.Null(login.WeaponSkillSet);
        Assert.Null(login.MagicSet);
        Assert.Null(login.SyndicateState);
    }

    [Fact]
    public async Task Complete_HandsEveryStepStateAndTheConnectionToTheCompletedBootstrap()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();
        ExistingCharacterGameConnection connection = login.Connection;
        CharacterItemSet itemSet = new(CharacterId, []);
        CharacterSocialRelationSet relationSet = new(CharacterId, []);
        CharacterWeaponSkillSet weaponSkillSet = new(CharacterId, []);
        CharacterMagicSet magicSet = new(CharacterId, []);
        CharacterSyndicateState syndicateState = new(CharacterId, membership: null, syndicate: null);

        login.ItemSet = itemSet;
        login.SocialRelationSet = relationSet;
        login.WeaponSkillSet = weaponSkillSet;
        login.MagicSet = magicSet;
        login.SyndicateState = syndicateState;
        login.ClientReportedSilentDataChecksum = 0x1234_5678;
        login.ClientReportedSilentDataVersion = 17;

        await using ExistingCharacterBootstrapCompleteConnection completed = login.Complete();

        Assert.Same(login.Profile, completed.Profile);
        Assert.Same(login.Map, completed.Map);
        Assert.Same(itemSet, completed.ItemSet);
        Assert.Same(relationSet, completed.SocialRelationSet);
        Assert.Same(weaponSkillSet, completed.WeaponSkillSet);
        Assert.Same(magicSet, completed.MagicSet);
        Assert.Same(syndicateState, completed.SyndicateState);
        Assert.Equal(0x1234_5678u, completed.ClientReportedSilentDataChecksum);
        Assert.Equal(17u, completed.ClientReportedSilentDataVersion);
        Assert.Throws<InvalidOperationException>(() => login.Connection);
        Assert.Throws<InvalidOperationException>(() => login.Complete());

        await login.DisposeAsync();

        Assert.Equal(0, harness.Transport.DisposeCount);
        Assert.Same(connection, completed.TakeConnection());

        await connection.DisposeAsync();

        Assert.Equal(1, harness.Transport.DisposeCount);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }

    [Fact]
    public async Task Complete_BeforeEveryStepRan_KeepsTheConnectionForCleanup()
    {
        await using LoginStepHarness harness = await LoginStepHarness.CreateAsync();
        ExistingCharacterLoginContext login = harness.CreateLoginContext();

        login.ItemSet = new CharacterItemSet(CharacterId, []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => login.Complete());

        Assert.Contains("friend-list", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(login.Connection);

        await login.DisposeAsync();

        Assert.Equal(1, harness.Transport.DisposeCount);
        Assert.False(harness.Presence.IsOnline(CharacterId));
    }
}
