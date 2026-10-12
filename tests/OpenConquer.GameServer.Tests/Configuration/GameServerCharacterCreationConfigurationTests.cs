using OpenConquer.Application.Characters.Creation;
using OpenConquer.GameServer.Configuration;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameServerCharacterCreationConfigurationTests
{
    [Fact]
    public void Load_WithoutStartingState_LeavesCreationUnconfigured()
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath());

        Assert.Null(configuration.CharacterCreationStartingState);
    }

    [Fact]
    public void Load_WithCompleteStartingState_CreatesValidatedSnapshot()
    {
        GameServerConfiguration configuration = LoadWithStartingState();

        CharacterCreationStartingState startingState = Assert.IsType<CharacterCreationStartingState>(
            configuration.CharacterCreationStartingState);

        Assert.Equal(1002u, startingState.MapId);
        Assert.Equal((ushort)0, startingState.PositionX);
        Assert.Equal((ushort)0, startingState.PositionY);

        CharacterCreationCandidate character = Assert.IsType<CharacterCreationCandidate>(
            startingState.CreateCandidate(42, "NewHero", 1003, 60));

        Assert.Equal((ushort)3, character.Strength);
        Assert.Equal((ushort)1, character.Agility);
        Assert.Equal((ushort)4, character.Vitality);
        Assert.Equal((ushort)2, character.Spirit);
        Assert.Equal((ushort)114, character.CurrentLife);
    }

    [Theory]
    [InlineData("GameServer:CharacterCreation:StartingMapId", "0")]
    [InlineData("GameServer:CharacterCreation:Allotments:0:Family", "0")]
    [InlineData("GameServer:CharacterCreation:Allotments:0:Family", "2")]
    [InlineData("GameServer:CharacterCreation:Allotments:0:Strength", "6")]
    [InlineData("GameServer:CharacterCreation:Allotments:5:Family", "1")]
    public void Load_InvalidStartingState_FailsBeforeHostConstruction(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            LoadWithStartingState(values => values[key] = value));
    }

    [Fact]
    public void Load_IncompleteStartingState_FailsBeforeHostConstruction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GameServerConfiguration.Load(GameServerConfigurationTests.CreateConfiguration(values =>
                values["GameServer:CharacterCreation:StartingMapId"] = "1002"), Path.GetTempPath()));
    }

    [Fact]
    public void Load_UnknownStartingStateProperty_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LoadWithStartingState(values =>
                values["GameServer:CharacterCreation:UnexpectedValue"] = "123"));
    }

    internal static GameServerConfiguration LoadWithStartingState(Action<Dictionary<string, string?>>? configure = null)
    {
        return GameServerConfiguration.Load(GameServerConfigurationTests.CreateConfiguration(values =>
        {
            Populate(values);
            configure?.Invoke(values);
        }), Path.GetTempPath());
    }

    private static void Populate(Dictionary<string, string?> values)
    {
        values["GameServer:CharacterCreation:StartingMapId"] = "1002";
        values["GameServer:CharacterCreation:StartingX"] = "0";
        values["GameServer:CharacterCreation:StartingY"] = "0";

        CharacterCreationStartingAllotment[] allotments =
        [
            new(1, 5, 2, 3, 0),
            new(2, 5, 2, 3, 0),
            new(4, 2, 7, 1, 0),
            new(5, 1, 8, 1, 0),
            new(6, 3, 1, 4, 2),
            new(10, 0, 2, 3, 5),
        ];

        for (int i = 0; i < allotments.Length; i++)
        {
            CharacterCreationStartingAllotment allotment = allotments[i];
            string prefix = $"GameServer:CharacterCreation:Allotments:{i}";

            values[$"{prefix}:Family"] = allotment.Family.ToString();
            values[$"{prefix}:Strength"] = allotment.Strength.ToString();
            values[$"{prefix}:Agility"] = allotment.Agility.ToString();
            values[$"{prefix}:Vitality"] = allotment.Vitality.ToString();
            values[$"{prefix}:Spirit"] = allotment.Spirit.ToString();
        }
    }
}
