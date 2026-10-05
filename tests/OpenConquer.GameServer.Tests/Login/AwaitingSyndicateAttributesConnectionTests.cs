using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class AwaitingSyndicateAttributesConnectionTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;

    [Fact]
    public async Task Constructor_NullDependencies_AreRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);
        GameMapEntryDefinition map = CreateMap();
        CharacterItemSet itemSet = CreateItemSet();
        CharacterSocialRelationSet socialRelationSet = CreateSocialRelationSet();
        CharacterWeaponSkillSet weaponSkillSet = CreateWeaponSkillSet();
        CharacterMagicSet magicSet = CreateMagicSet();

        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(null!, map, itemSet, socialRelationSet, weaponSkillSet, magicSet));
        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(connection, null!, itemSet, socialRelationSet, weaponSkillSet, magicSet));
        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(connection, map, null!, socialRelationSet, weaponSkillSet, magicSet));
        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(connection, map, itemSet, null!, weaponSkillSet, magicSet));
        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(connection, map, itemSet, socialRelationSet, null!, magicSet));
        Assert.Throws<ArgumentNullException>(() => new AwaitingSyndicateAttributesConnection(connection, map, itemSet, socialRelationSet, weaponSkillSet, null!));
    }

    [Fact]
    public async Task Constructor_MapDoesNotMatchPersistedCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);
        GameMapEntryDefinition map = new(mapId: 1003, mapDataId: 1015, flags: 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AwaitingSyndicateAttributesConnection(connection, map, CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet()));

        Assert.Equal("map", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_ItemSetBelongsToDifferentCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AwaitingSyndicateAttributesConnection(connection, CreateMap(), CreateItemSet(CharacterId + 1), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet()));

        Assert.Equal("itemSet", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_SocialRelationSetBelongsToDifferentCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AwaitingSyndicateAttributesConnection(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(CharacterId + 1), CreateWeaponSkillSet(), CreateMagicSet()));

        Assert.Equal("socialRelationSet", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WeaponSkillSetBelongsToDifferentCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AwaitingSyndicateAttributesConnection(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(CharacterId + 1), CreateMagicSet()));

        Assert.Equal("weaponSkillSet", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_MagicSetBelongsToDifferentCharacter_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AwaitingSyndicateAttributesConnection(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet(CharacterId + 1)));

        Assert.Equal("magicSet", exception.ParamName);
    }

    [Fact]
    public async Task DisposeWithoutTransfer_DisposesOwnedConnection()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);
        AwaitingSyndicateAttributesConnection owner = new(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet());

        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task ConcurrentTake_AllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);
        AwaitingSyndicateAttributesConnection owner = new(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet());

        ExistingCharacterGameConnection? first = null;
        ExistingCharacterGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        Assert.True(first is not null ^ second is not null,
            $"Expected exactly one successful transfer. First: {first is not null}, second: {second is not null}.");
        Assert.Same(connection, first ?? second);
        Assert.IsType<InvalidOperationException>(firstFailure ?? secondFailure);
        Assert.True(firstFailure is null ^ secondFailure is null,
            $"Expected exactly one failed transfer. First failure: {firstFailure?.GetType().Name ?? "none"}, second failure: {secondFailure?.GetType().Name ?? "none"}.");
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task TakeAndDisposeRace_AllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateExistingCharacterConnectionAsync(transport);
        AwaitingSyndicateAttributesConnection owner = new(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet());

        ExistingCharacterGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(transferred is not null ^ takeFailure is InvalidOperationException,
            $"Expected exactly one ownership winner. Transferred: {transferred is not null}, failure: {takeFailure?.GetType().Name ?? "none"}.");

        if (transferred is not null)
        {
            Assert.Same(connection, transferred);
            Assert.Equal(0, transport.DisposeCount);
            await transferred.DisposeAsync();
        }
        else
        {
            Assert.Equal(1, transport.DisposeCount);
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    private static void TryTake(AwaitingSyndicateAttributesConnection owner, out ExistingCharacterGameConnection? connection, out Exception? failure)
    {
        try
        {
            connection = owner.TakeConnection();
            failure = null;
        }
        catch (Exception exception)
        {
            connection = null;
            failure = exception;
        }
    }

    private static async Task<AuthenticatedGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        return new AuthenticatedGameConnection(AccountId, Username, SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
    }

    private static async Task<ExistingCharacterGameConnection> CreateExistingCharacterConnectionAsync(FakeGameTransportConnection transport)
    {
        AuthenticatedGameConnection connection = await CreateConnectionAsync(transport);
        CharacterLoginProfile profile = CreateProfile();
        CharacterPresenceDirectory presence = new();
        return new ExistingCharacterGameConnection(connection, profile, presence.Register(profile.Identity.CharacterId));
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, pkPoints: 0, titleId: 0, enlightenmentPoints: 0, location);
    }

    private static CharacterItemSet CreateItemSet(uint characterId = CharacterId) => new(characterId, []);

    private static CharacterSocialRelationSet CreateSocialRelationSet(uint characterId = CharacterId) => new(characterId, []);

    private static CharacterWeaponSkillSet CreateWeaponSkillSet(uint characterId = CharacterId) => new(characterId, []);

    private static CharacterMagicSet CreateMagicSet(uint characterId = CharacterId) => new(characterId, []);

    private static GameMapEntryDefinition CreateMap() => new(mapId: 1002, mapDataId: 1015, flags: 0);
}
