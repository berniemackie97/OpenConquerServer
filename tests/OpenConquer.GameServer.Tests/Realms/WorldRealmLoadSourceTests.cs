using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Realms;
using OpenConquer.GameServer.Realms;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;

namespace OpenConquer.GameServer.Tests.Realms;

public sealed class WorldRealmLoadSourceTests
{
    private const uint MapId = 1002;

    [Fact]
    public async Task Sample_ReportsNormalLoadAndResidentCountBelowTheCharacterLimit()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 2);
        runtime.Start();
        await runtime.AdmitCharacterAsync(CreateProfile(CharacterIdentityPolicy.FirstPlayerEntityId), TestContext.Current.CancellationToken);

        RealmLoadSample sample = new WorldRealmLoadSource(runtime).Sample();

        Assert.Equal(RealmLoad.Normal, sample.Load);
        Assert.Equal(1, sample.OnlineCount);
    }

    [Fact]
    public async Task Sample_ReportsFullAtTheCharacterLimit()
    {
        await using WorldRuntime runtime = CreateRuntime(maxCharacters: 1);
        runtime.Start();
        await runtime.AdmitCharacterAsync(CreateProfile(CharacterIdentityPolicy.FirstPlayerEntityId), TestContext.Current.CancellationToken);

        RealmLoadSample sample = new WorldRealmLoadSource(runtime).Sample();

        Assert.Equal(RealmLoad.Full, sample.Load);
        Assert.Equal(1, sample.OnlineCount);
    }

    [Fact]
    public void Constructor_RejectsMissingRuntime()
    {
        Assert.Throws<ArgumentNullException>(() => new WorldRealmLoadSource(null!));
    }

    private static WorldRuntime CreateRuntime(int maxCharacters)
    {
        WorldMapContentCatalog catalog = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(MapId, 1015, 0));

        return new WorldRuntime(catalog, new WorldExecutionOptions(workerCount: 1, partitionQueueCapacity: 8, maxCommandsPerTurn: 8, maxCharacters, maxCharactersPerMap: maxCharacters));
    }

    private static CharacterLoginProfile CreateProfile(uint characterId)
    {
        return new CharacterLoginProfile(new CharacterLoginIdentity(characterId, 42, "Bernie"), new CharacterAppearance(composite: 1003, hair: 410),
            new CharacterProgression(level: 1, experience: 0, profession: 10, firstProfession: 0, previousProfession: 0, rebirthCount: 0, preRebirthLevel: 0),
            new CharacterAttributes(10, 10, 10, 10, 0), new CharacterVitals(100, 0), new CharacterEconomy(0, 0, 0),
            pkPoints: 0, titleId: 0, enlightenmentPoints: 0, new CharacterLocation(MapId, 0, 0));
    }
}
