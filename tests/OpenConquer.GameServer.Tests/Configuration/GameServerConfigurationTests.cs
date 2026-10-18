using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameServerConfigurationTests
{
    [Fact]
    public void Load_ValidConfigurationPreservesDatabaseAndWorldSettings()
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(CreateConfiguration(), Path.GetTempPath());

        Assert.Equal("Server=localhost;Database=accounts", configuration.AccountConnectionString);
        Assert.Equal("Server=localhost;Database=game", configuration.GameConnectionString);
        Assert.Equal(1u, configuration.RealmId.Value);
        Assert.Equal(4, configuration.World.WorkerCount);
        Assert.Equal(128, configuration.World.PartitionQueueCapacity);
        Assert.Equal(32, configuration.World.MaxCommandsPerTurn);
        Assert.Equal(1000, configuration.World.MaxCharacters);
        Assert.Equal(200, configuration.World.MaxCharactersPerMap);
        Assert.Equal(7, configuration.ActiveVerificationKeyId);
        Assert.Single(configuration.VerificationKeys);
        Assert.Equal(7, configuration.VerificationKeys[0].Key);
        Assert.Equal(Path.GetFullPath("content/maps/map-definitions.json", Path.GetTempPath()), configuration.MapDefinitions.FilePath);
        Assert.Equal(Path.GetFullPath("content/maps/terrains", Path.GetTempPath()), configuration.TerrainDirectory);
        Assert.Equal(Path.GetFullPath("content/maps/collision", Path.GetTempPath()), configuration.CollisionDirectory);
        Assert.Equal(Path.GetFullPath("content/maps/map-transitions.json", Path.GetTempPath()), configuration.TransitionsPath);
        Assert.Equal(1048576, configuration.MaximumTransitionFileBytes);
        Assert.Equal(1000, configuration.MaximumTransitions);
    }

    [Fact]
    public void Load_MissingOrZeroRealmIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(
            CreateConfiguration(values => values.Remove("GameServer:RealmId")), Path.GetTempPath()));
        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(
            CreateConfiguration(values => values["GameServer:RealmId"] = "0"), Path.GetTempPath()));
    }

    [Theory]
    [InlineData("ConnectionStrings:Accounts")]
    [InlineData("ConnectionStrings:Game")]
    public void Load_MissingDatabaseConnectionIsRejected(string key)
    {
        IConfiguration configuration = CreateConfiguration(values => values.Remove(key));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => GameServerConfiguration.Load(configuration, Path.GetTempPath()));

        Assert.Contains(key, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GameServer:World:WorkerCount", "0")]
    [InlineData("GameServer:World:WorkerCount", "1025")]
    [InlineData("GameServer:World:PartitionQueueCapacity", "0")]
    [InlineData("GameServer:World:PartitionQueueCapacity", "65537")]
    [InlineData("GameServer:World:MaxCommandsPerTurn", "4097")]
    [InlineData("GameServer:World:MaxCharacters", "0")]
    [InlineData("GameServer:World:MaxCharactersPerMap", "1001")]
    [InlineData("GameServer:Content:MaximumDefinitionFileBytes", "0")]
    [InlineData("GameServer:Content:MaximumDefinitions", "100001")]
    [InlineData("GameServer:Content:MaximumTransitionFileBytes", "0")]
    [InlineData("GameServer:Content:MaximumTransitionFileBytes", "67108865")]
    [InlineData("GameServer:Content:MaximumTransitions", "0")]
    [InlineData("GameServer:Content:MaximumTransitions", "100001")]
    public void Load_InvalidResourceBudgetIsRejected(string key, string value)
    {
        IConfiguration configuration = CreateConfiguration(values => values[key] = value);

        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
    }

    [Theory]
    [InlineData("GameServer:Content:DefinitionsPath")]
    [InlineData("GameServer:Content:TerrainDirectory")]
    [InlineData("GameServer:Content:CollisionDirectory")]
    [InlineData("GameServer:Content:TransitionsPath")]
    public void Load_MissingContentPathIsRejected(string key)
    {
        IConfiguration configuration = CreateConfiguration(values => values.Remove(key));

        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void Load_UnknownConfigurationPropertyIsRejected()
    {
        IConfiguration configuration = CreateConfiguration(values => values["GameServer:World:UnexpectedLimit"] = "100");

        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void Load_DuplicateVerificationKeyIdentityIsRejected()
    {
        IConfiguration configuration = CreateConfiguration(values =>
        {
            values["GameServer:GameLoginTickets:VerificationKeys:1:Id"] = "7";
            values["GameServer:GameLoginTickets:VerificationKeys:1:EncodedKey"] = VerificationKey;
        });

        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void ConfigureServices_InvalidVerificationKeyMaterialIsRejectedBeforeHostStartup()
    {
        IConfiguration configuration = CreateConfiguration(values =>
            values["GameServer:GameLoginTickets:VerificationKeys:0:EncodedKey"] = "invalid");

        GameServerConfiguration settings = GameServerConfiguration.Load(configuration, Path.GetTempPath());
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        Assert.Throws<ArgumentException>(() => GameServerHost.ConfigureServices(builder.Services, settings));
    }

    internal const string VerificationKey = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";

    internal static IConfiguration CreateConfiguration(Action<Dictionary<string, string?>>? configure = null)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Accounts"] = "Server=localhost;Database=accounts",
            ["ConnectionStrings:Game"] = "Server=localhost;Database=game",
            ["GameServer:RealmId"] = "1",

            ["GameServer:Content:DefinitionsPath"] = "content/maps/map-definitions.json",
            ["GameServer:Content:TerrainDirectory"] = "content/maps/terrains",
            ["GameServer:Content:CollisionDirectory"] = "content/maps/collision",
            ["GameServer:Content:TransitionsPath"] = "content/maps/map-transitions.json",
            ["GameServer:Content:MaximumTransitionFileBytes"] = "1048576",
            ["GameServer:Content:MaximumTransitions"] = "1000",
            ["GameServer:Content:MaximumDefinitionFileBytes"] = "16777216",
            ["GameServer:Content:MaximumDefinitions"] = "10000",

            ["GameServer:World:WorkerCount"] = "4",
            ["GameServer:World:PartitionQueueCapacity"] = "128",
            ["GameServer:World:MaxCommandsPerTurn"] = "32",
            ["GameServer:World:MaxCharacters"] = "1000",
            ["GameServer:World:MaxCharactersPerMap"] = "200",

            ["GameServer:GameLoginTickets:ActiveVerificationKeyId"] = "7",
            ["GameServer:GameLoginTickets:VerificationKeys:0:Id"] = "7",
            ["GameServer:GameLoginTickets:VerificationKeys:0:EncodedKey"] = VerificationKey,
        };

        configure?.Invoke(values);

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
