using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Login.WorldEntry;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.GameServer.World.Presence;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class ExistingCharacterBootstrapCompleteConnectionTests
{
    private const uint AccountId = 42;
    private const string Username = "Bernie";
    private const uint SessionUid = 0x1020_3040;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;
    private const uint CharacterId = CharacterIdentityPolicy.FirstPlayerEntityId;
    private const uint ClientChecksum = 0xAABB_CCDD;
    private const uint ClientVersion = 17;

    [Fact]
    public async Task Constructor_NullDependencies_AreRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);

        GameMapEntryDefinition map = CreateMap();
        CharacterItemSet items = CreateItemSet();
        CharacterSocialRelationSet relations = CreateSocialRelationSet();
        CharacterWeaponSkillSet weaponSkills = CreateWeaponSkillSet();
        CharacterMagicSet magic = CreateMagicSet();
        CharacterSyndicateState syndicate = CreateSyndicateState();

        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(null!, map, items,
            relations, weaponSkills, magic, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, null!, items,
            relations, weaponSkills, magic, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, map, null!,
            relations, weaponSkills, magic, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, map, items,
            null!, weaponSkills, magic, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, map, items,
            relations, null!, magic, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, map, items,
            relations, weaponSkills, null!, syndicate, ClientChecksum, ClientVersion));
        Assert.Throws<ArgumentNullException>(() => new ExistingCharacterBootstrapCompleteConnection(connection, map, items,
            relations, weaponSkills, magic, null!, ClientChecksum, ClientVersion));
    }

    [Fact]
    public async Task Constructor_WrongMap_IsRejected()
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);
        GameMapEntryDefinition wrongMap = new(1003, 1015, 0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ExistingCharacterBootstrapCompleteConnection(connection, wrongMap, CreateItemSet(), CreateSocialRelationSet(),
                CreateWeaponSkillSet(), CreateMagicSet(), CreateSyndicateState(), ClientChecksum, ClientVersion));

        Assert.Equal("map", exception.ParamName);
    }

    [Theory]
    [InlineData("itemSet")]
    [InlineData("socialRelationSet")]
    [InlineData("weaponSkillSet")]
    [InlineData("magicSet")]
    [InlineData("syndicateState")]
    public async Task Constructor_StateBelongsToAnotherCharacter_IsRejected(string parameterName)
    {
        FakeGameTransportConnection transport = new();
        await using ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);

        uint otherCharacterId = CharacterId + 1;

        CharacterItemSet items = CreateItemSet(parameterName == "itemSet" ? otherCharacterId : CharacterId);
        CharacterSocialRelationSet relations = CreateSocialRelationSet(parameterName == "socialRelationSet" ? otherCharacterId : CharacterId);
        CharacterWeaponSkillSet weaponSkills = CreateWeaponSkillSet(parameterName == "weaponSkillSet" ? otherCharacterId : CharacterId);
        CharacterMagicSet magic = CreateMagicSet(parameterName == "magicSet" ? otherCharacterId : CharacterId);
        CharacterSyndicateState syndicate = CreateSyndicateState(parameterName == "syndicateState" ? otherCharacterId : CharacterId);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new ExistingCharacterBootstrapCompleteConnection(connection, CreateMap(), items, relations, weaponSkills,
                magic, syndicate, ClientChecksum, ClientVersion));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public async Task Constructor_PreservesAllSnapshotsAndUntrustedReportValues()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);

        GameMapEntryDefinition map = CreateMap();
        CharacterItemSet items = CreateItemSet();
        CharacterSocialRelationSet relations = CreateSocialRelationSet();
        CharacterWeaponSkillSet weaponSkills = CreateWeaponSkillSet();
        CharacterMagicSet magic = CreateMagicSet();
        CharacterSyndicateState syndicate = CreateSyndicateState();

        await using ExistingCharacterBootstrapCompleteConnection owner = new(connection, map, items, relations,
            weaponSkills, magic, syndicate, ClientChecksum, ClientVersion);

        Assert.Same(connection.Profile, owner.Profile);
        Assert.Same(map, owner.Map);
        Assert.Same(items, owner.ItemSet);
        Assert.Same(relations, owner.SocialRelationSet);
        Assert.Same(weaponSkills, owner.WeaponSkillSet);
        Assert.Same(magic, owner.MagicSet);
        Assert.Same(syndicate, owner.SyndicateState);
        Assert.Equal(ClientChecksum, owner.ClientReportedSilentDataChecksum);
        Assert.Equal(ClientVersion, owner.ClientReportedSilentDataVersion);
        Assert.Equal(0, transport.DisposeCount);
    }

    [Fact]
    public async Task Constructor_AllowsZeroAndMaximumClientReportedValues()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);

        await using ExistingCharacterBootstrapCompleteConnection owner = new(connection, CreateMap(), CreateItemSet(),
            CreateSocialRelationSet(), CreateWeaponSkillSet(), CreateMagicSet(), CreateSyndicateState(), 0, uint.MaxValue);

        Assert.Equal(0u, owner.ClientReportedSilentDataChecksum);
        Assert.Equal(uint.MaxValue, owner.ClientReportedSilentDataVersion);
    }

    [Fact]
    public async Task DisposeWithoutTransfer_DisposesConnectionExactlyOnce()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);
        ExistingCharacterBootstrapCompleteConnection owner = CreateOwner(connection);

        await owner.DisposeAsync();
        await owner.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());
    }

    [Fact]
    public async Task Transfer_PreservesOwnershipAndLeavesConnectionAlive()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);
        ExistingCharacterBootstrapCompleteConnection owner = CreateOwner(connection);

        ExistingCharacterGameConnection transferred = owner.TakeConnection();

        Assert.Same(connection, transferred);
        Assert.Throws<InvalidOperationException>(() => owner.TakeConnection());

        await owner.DisposeAsync();

        Assert.Equal(0, transport.DisposeCount);

        await transferred.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentTake_AllowsExactlyOneWinner()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);
        ExistingCharacterBootstrapCompleteConnection owner = CreateOwner(connection);

        ExistingCharacterGameConnection? first = null;
        ExistingCharacterGameConnection? second = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out first, out firstFailure), TestContext.Current.CancellationToken),
            Task.Run(() => TryTake(owner, out second, out secondFailure), TestContext.Current.CancellationToken));

        Assert.True(first is not null ^ second is not null);
        Assert.Same(connection, first ?? second);
        Assert.IsType<InvalidOperationException>(firstFailure ?? secondFailure);
        Assert.True(firstFailure is null ^ secondFailure is null);
        Assert.Equal(0, transport.DisposeCount);

        await (first ?? second)!.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task TakeAndDisposeRace_AllowsExactlyOneOwner()
    {
        FakeGameTransportConnection transport = new();
        ExistingCharacterGameConnection connection = await CreateConnectionAsync(transport);
        ExistingCharacterBootstrapCompleteConnection owner = CreateOwner(connection);

        ExistingCharacterGameConnection? transferred = null;
        Exception? takeFailure = null;

        await Task.WhenAll(
            Task.Run(() => TryTake(owner, out transferred, out takeFailure), TestContext.Current.CancellationToken),
            Task.Run(async () => await owner.DisposeAsync(), TestContext.Current.CancellationToken));

        Assert.True(transferred is not null ^ takeFailure is InvalidOperationException);

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

    private static void TryTake(ExistingCharacterBootstrapCompleteConnection owner, out ExistingCharacterGameConnection? connection, out Exception? failure)
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

    private static ExistingCharacterBootstrapCompleteConnection CreateOwner(ExistingCharacterGameConnection connection) =>
        new(connection, CreateMap(), CreateItemSet(), CreateSocialRelationSet(), CreateWeaponSkillSet(),
            CreateMagicSet(), CreateSyndicateState(), ClientChecksum, ClientVersion);

    private static async Task<ExistingCharacterGameConnection> CreateConnectionAsync(FakeGameTransportConnection transport)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(transport, TestContext.Current.CancellationToken);
        AuthenticatedGameConnection authenticated = new(AccountId, Username, SessionUid, LocaleTag,
            HardwareAddress, ResourceVersion, session);
        CharacterLoginProfile profile = CreateProfile();
        CharacterPresenceDirectory presence = new();

        return new ExistingCharacterGameConnection(authenticated, profile, presence.Register(profile.Identity.CharacterId));
    }

    private static CharacterLoginProfile CreateProfile()
    {
        CharacterLoginIdentity identity = new(CharacterId, AccountId, Username);
        CharacterAppearance appearance = new(composite: 1003, hair: 410);
        CharacterProgression progression = new(level: 1, experience: 0, profession: 10, firstProfession: 0,
            previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0);
        CharacterAttributes attributes = new(10, 10, 10, 10, 0);
        CharacterVitals vitals = new(100, 0);
        CharacterEconomy economy = new(0, 0, 0);
        CharacterLocation location = new(mapId: 1002, x: 430, y: 378);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy,
            pkPoints: 0, titleId: 0, enlightenmentPoints: 0, location);
    }

    private static GameMapEntryDefinition CreateMap() => new(1002, 1015, 0);
    private static CharacterItemSet CreateItemSet(uint characterId = CharacterId) => new(characterId, []);
    private static CharacterSocialRelationSet CreateSocialRelationSet(uint characterId = CharacterId) => new(characterId, []);
    private static CharacterWeaponSkillSet CreateWeaponSkillSet(uint characterId = CharacterId) => new(characterId, []);
    private static CharacterMagicSet CreateMagicSet(uint characterId = CharacterId) => new(characterId, []);

    private static CharacterSyndicateState CreateSyndicateState(uint characterId = CharacterId) =>
        new(characterId, membership: null, syndicate: null);
}
