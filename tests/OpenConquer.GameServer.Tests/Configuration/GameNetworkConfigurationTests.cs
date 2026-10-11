using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Hosting;

namespace OpenConquer.GameServer.Tests.Configuration;

public sealed class GameNetworkConfigurationTests
{
    [Fact]
    public void Load_NetworkEnabledConfigurationPreservesValidatedIngressAndItemContent()
    {
        GameServerConfiguration configuration = CreateEnabledConfiguration();

        GameNetworkOptions network = Assert.IsType<GameNetworkOptions>(configuration.Network);

        Assert.Equal(IPAddress.Loopback, network.EndPoint.Address);
        Assert.Equal(5816, network.EndPoint.Port);
        Assert.Equal(128, network.ListenBacklog);
        Assert.Equal(64, network.AdmissionCapacity);
        Assert.Equal(4, network.MaximumConnectionsPerSource);
        Assert.Equal(8, network.WorkerCount);
        Assert.Equal(TimeSpan.FromSeconds(45), network.ConnectionTimeout);

        Assert.NotNull(configuration.ItemTypes);
        Assert.Equal(Path.GetFullPath("content/items/item-types.json", Path.GetTempPath()), configuration.ItemTypes.FilePath);
        Assert.Equal(16_777_216, configuration.ItemTypes.MaximumFileLengthBytes);
        Assert.Equal(200_000, configuration.ItemTypes.MaximumDefinitions);
    }

    [Fact]
    public async Task ConfigureServices_NetworkEnabledHostRegistersWorldBeforeIngress()
    {
        GameServerConfiguration configuration = CreateEnabledConfiguration();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        GameServerHost.ConfigureServices(builder.Services, configuration);

        IHost host = builder.Build();

        try
        {
            IHostedService[] services = host.Services.GetServices<IHostedService>().ToArray();

            Assert.Collection(services,
                service => Assert.IsType<GameServerWorldHostedService>(service),
                service => Assert.IsType<GameLoginRuntimeHostedService>(service));
        }
        finally
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("GameServer:Network:BindAddress", "not-an-address")]
    [InlineData("GameServer:Network:BindAddress", "::1")]
    [InlineData("GameServer:Network:Port", "0")]
    [InlineData("GameServer:Network:Port", "65536")]
    [InlineData("GameServer:Network:ListenBacklog", "0")]
    [InlineData("GameServer:Network:ListenBacklog", "4097")]
    [InlineData("GameServer:Network:AdmissionCapacity", "0")]
    [InlineData("GameServer:Network:AdmissionCapacity", "65537")]
    [InlineData("GameServer:Network:MaximumConnectionsPerSource", "0")]
    [InlineData("GameServer:Network:MaximumConnectionsPerSource", "1025")]
    [InlineData("GameServer:Network:WorkerCount", "0")]
    [InlineData("GameServer:Network:WorkerCount", "1025")]
    [InlineData("GameServer:Network:ConnectionTimeout", "00:00:00")]
    [InlineData("GameServer:Network:ConnectionTimeout", "00:11:00")]
    [InlineData("GameServer:Items:MaximumFileBytes", "0")]
    [InlineData("GameServer:Items:MaximumDefinitions", "0")]
    [InlineData("GameServer:Items:FilePath", "")]
    public void Load_NetworkEnabledRejectsInvalidConfiguration(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(values =>
            {
                AddNetworkSettings(values);
                values[key] = value;
            }),
            Path.GetTempPath()));
    }

    [Fact]
    public void Load_NetworkDisabledPreservesWorldOnlyStartupMode()
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath());

        Assert.Null(configuration.Network);
        Assert.Null(configuration.ItemTypes);
    }

    [Fact]
    public void Load_NetworkEnabledRejectsUnknownProperty()
    {
        Assert.Throws<InvalidOperationException>(() => GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(values =>
            {
                AddNetworkSettings(values);
                values["GameServer:Network:UnknownSetting"] = "1";
            }),
            Path.GetTempPath()));
    }

    private static GameServerConfiguration CreateEnabledConfiguration()
    {
        return GameServerConfiguration.Load(
            GameServerConfigurationTests.CreateConfiguration(AddNetworkSettings),
            Path.GetTempPath());
    }

    private static void AddNetworkSettings(Dictionary<string, string?> values)
    {
        values["GameServer:Network:Enabled"] = "true";
        values["GameServer:Network:BindAddress"] = "127.0.0.1";
        values["GameServer:Network:Port"] = "5816";
        values["GameServer:Network:ListenBacklog"] = "128";
        values["GameServer:Network:AdmissionCapacity"] = "64";
        values["GameServer:Network:MaximumConnectionsPerSource"] = "4";
        values["GameServer:Network:WorkerCount"] = "8";
        values["GameServer:Network:ConnectionTimeout"] = "00:00:45";

        values["GameServer:Items:FilePath"] = "content/items/item-types.json";
        values["GameServer:Items:MaximumFileBytes"] = "16777216";
        values["GameServer:Items:MaximumDefinitions"] = "200000";
    }
}
