using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Tests.World;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameServerCharacterCreationReadinessTests
{
    [Fact]
    public void Validate_UnconfiguredStartingState_DoesNotRequireAnExtraMap()
    {
        GameServerCharacterCreationReadiness readiness = new(null);
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(1002, 1015, 0));

        readiness.Validate(maps);
    }

    [Fact]
    public void Validate_ConfiguredStartingState_RequiresExistingCanonicalMap()
    {
        GameServerCharacterCreationReadiness readiness = new(CreateStartingState(mapId: 1010));
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(1002, 1015, 0));

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => readiness.Validate(maps));

        Assert.Contains("1010", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ConfiguredStartingState_RejectsOutOfBoundsX()
    {
        GameServerCharacterCreationReadiness readiness = new(CreateStartingState(x: 1));
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(1002, 1015, 0));

        Assert.Throws<InvalidDataException>(() => readiness.Validate(maps));
    }

    [Fact]
    public void Validate_ConfiguredStartingState_RejectsOutOfBoundsY()
    {
        GameServerCharacterCreationReadiness readiness = new(CreateStartingState(y: 1));
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(1002, 1015, 0));

        Assert.Throws<InvalidDataException>(() => readiness.Validate(maps));
    }

    [Fact]
    public void Validate_ConfiguredStartingState_AcceptsExistingInBoundsSpawn()
    {
        GameServerCharacterCreationReadiness readiness = new(CreateStartingState());
        WorldMapContentCatalog maps = WorldMapTestContent.CreateCatalog(WorldMapTestContent.Create(1002, 1015, 0));

        readiness.Validate(maps);
    }

    [Fact]
    public void Validate_RejectsNullMapCatalogEvenWhenNotConfigured()
    {
        GameServerCharacterCreationReadiness readiness = new(null);

        Assert.Throws<ArgumentNullException>(() => readiness.Validate(null!));
    }

    private static CharacterCreationStartingState CreateStartingState(uint mapId = 1002, ushort x = 0, ushort y = 0)
    {
        CharacterCreationStartingAllotment[] allotments =
        [
            new(1, 5, 2, 3, 0),
            new(2, 5, 2, 3, 0),
            new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0),
            new(6, 3, 1, 4, 2),
            new(10, 0, 2, 3, 5),
        ];

        return new CharacterCreationStartingState(mapId, x, y, allotments);
    }
}
