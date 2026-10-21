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
    public void Load_NetworkIngressWithoutLocationWriterFailsClosed()
    {
        IConfiguration configuration = CreateConfiguration(EnableNetwork);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
        Assert.Contains("ConnectionStrings:GameLocationWriter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NetworkIngressWithDedicatedWriterIsValid()
    {
        IConfiguration configuration = CreateConfiguration(values =>
        {
            EnableNetwork(values);
            values["ConnectionStrings:GameLocationWriter"] = "Server=localhost;Database=game;User ID=location_writer;Password=secret";
        });

        GameServerConfiguration settings = GameServerConfiguration.Load(configuration, Path.GetTempPath());
        Assert.NotNull(settings.Network);
        Assert.NotNull(settings.LocationWriteConnectionString);
    }

    [Theory]
    [InlineData("Server=localhost;Database=other;User ID=location_writer;Password=secret")]
    [InlineData("Server=other;Database=game;User ID=location_writer;Password=secret")]
    [InlineData("Server=localhost;Database=game;User ID=root;Password=secret")]
    public void Load_LocationWriterWithWrongDatabaseOrIdentityIsRejected(string writer)
    {
        IConfiguration configuration = CreateConfiguration(values => values["ConnectionStrings:GameLocationWriter"] = writer);
        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void Load_LocationWriterReusingReadOnlyUserIdIsRejectedEvenWithDifferentPassword()
    {
        IConfiguration configuration = CreateConfiguration(values =>
        {
            values["ConnectionStrings:Game"] = "Server=localhost;Database=game;User ID=game;Password=readonly";
            values["ConnectionStrings:GameLocationWriter"] = "Server=localhost;Database=game;User ID=game;Password=writer";
        });

        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(configuration, Path.GetTempPath()));
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

    private static void EnableNetwork(Dictionary<string, string?> values)
    {
        values["GameServer:Network:Enabled"] = "true";
        values["GameServer:Network:BindAddress"] = "127.0.0.1";
        values["GameServer:Network:Port"] = "5816";
        values["GameServer:Network:ListenBacklog"] = "32";
        values["GameServer:Network:AdmissionCapacity"] = "32";
        values["GameServer:Network:MaximumConnectionsPerSource"] = "8";
        values["GameServer:Network:WorkerCount"] = "2";
        values["GameServer:Network:ConnectionTimeout"] = "00:01:00";
        values["GameServer:RealmStatus:ReportInterval"] = "00:00:10";
        values["GameServer:Items:FilePath"] = "content/items/item-types.json";
        values["GameServer:Items:MaximumFileBytes"] = "16777216";
        values["GameServer:Items:MaximumDefinitions"] = "200000";
    }

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
