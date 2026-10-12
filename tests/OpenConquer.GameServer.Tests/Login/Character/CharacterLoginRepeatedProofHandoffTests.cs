using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.Domain.Characters;
using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Tests.Login.Character;

public sealed class CharacterLoginRepeatedProofHandoffTests
{
    private const uint AccountId = 42;
    private const uint SessionUid = 0x1020_3040;
    private const uint AuthenticationKey = 0x5060_7080;
    private const ushort LocaleTag = 0x6E45;
    private const ulong HardwareAddress = 0x0000_6655_4433_2211;
    private const int ResourceVersion = 5517;

    [Fact]
    public async Task ProcessAsync_ExistingCharacterRouteDiscardsRepeatedProof()
    {
        await VerifyRouteAsync(CharacterLoginResolution.ExistingCharacter(CreateProfile()), expectedProofAccepted: false);
    }

    [Fact]
    public async Task ProcessAsync_CreationRouteRetainsOneRepeatedProof()
    {
        await VerifyRouteAsync(CharacterLoginResolution.CharacterCreation(), expectedProofAccepted: true);
    }

    private static async Task VerifyRouteAsync(CharacterLoginResolution resolution, bool expectedProofAccepted)
    {
        GameConnectionSession session = await GameConnectionSession.OpenAsync(new FakeGameTransportConnection(), TestContext.Current.CancellationToken);
        AuthenticatedGameConnection connection = new(AccountId, "Bernie", SessionUid, LocaleTag, HardwareAddress, ResourceVersion, session);
        GameLoginProof proof = CreateProof();
        connection.ArmRepeatedLoginProof(proof);

        await using GameConnectionAuthenticationResult authentication = GameConnectionAuthenticationResult.Authenticated(connection);
        CharacterLoginHandoffProcessor processor = new(new StubResolver(resolution));

        await using CharacterLoginHandoffResult handoff = await processor.ProcessAsync(authentication, TestContext.Current.CancellationToken);
        await using AuthenticatedGameConnection owned = handoff.TakeConnection();

        Assert.Equal(expectedProofAccepted, owned.TryConsumeRepeatedLoginProof(proof));
        Assert.False(owned.TryConsumeRepeatedLoginProof(proof));
    }

    private static GameLoginProof CreateProof()
    {
        return new GameLoginProof(SessionUid, AuthenticationKey, GameLoginProof.ExpectedMode, LocaleTag, HardwareAddress, ResourceVersion);
    }

    private static CharacterLoginProfile CreateProfile()
    {
        return new CharacterLoginProfile(
            new CharacterLoginIdentity(CharacterIdentityPolicy.FirstPlayerEntityId, AccountId, "Bernie"),
            new CharacterAppearance(composite: 2011003, hair: 410),
            new CharacterProgression(level: 1, experience: 0, profession: 10, firstProfession: 10, previousProfession: 10, rebirthCount: 0, preRebirthLevel: 0),
            new CharacterAttributes(5, 2, 3, 0, 0),
            new CharacterVitals(93, 0),
            new CharacterEconomy(0, 0, 0),
            pkPoints: 0,
            titleId: 0,
            enlightenmentPoints: 0,
            new CharacterLocation(mapId: 1002, x: 0, y: 0));
    }

    private sealed class StubResolver(CharacterLoginResolution resolution) : ICharacterLoginResolver
    {
        public ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(AccountId, accountId);
            return ValueTask.FromResult(resolution);
        }
    }
}
