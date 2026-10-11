using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenConquer.AccountServer.Configuration;
using OpenConquer.AccountServer.Hosting;

namespace OpenConquer.AccountServer.Tests.Configuration;

public sealed class AccountServerDeploymentConfigurationTests
{
    [Fact]
    public void ProductionConfiguration_DoesNotSupplyDeploymentSettings()
    {
        IConfiguration configuration = LoadConfiguration(false);

        Assert.Null(configuration.GetConnectionString("Accounts"));
        Assert.Null(configuration["AccountServer:Network:BindAddress"]);
        Assert.Null(configuration["AccountServer:Network:LoginPort"]);
        Assert.Null(configuration["AccountServer:Network:GameServerAddress"]);
        Assert.Null(configuration["AccountServer:Network:GameServerPort"]);
        Assert.Null(configuration["AccountServer:GameLoginTickets:ActiveVerificationKeyId"]);
        Assert.False(configuration.GetSection("AccountServer:GameLoginTickets:VerificationKeys").Exists());
    }

    [Fact]
    public void DevelopmentConfiguration_SuppliesLoopbackEndpointsWithoutSecrets()
    {
        IConfiguration configuration = LoadConfiguration(true);

        Assert.Equal("127.0.0.1", configuration["AccountServer:Network:BindAddress"]);
        Assert.Equal("9958", configuration["AccountServer:Network:LoginPort"]);
        Assert.Equal("127.0.0.1", configuration["AccountServer:Network:GameServerAddress"]);
        Assert.Equal("5816", configuration["AccountServer:Network:GameServerPort"]);

        Assert.Null(configuration.GetConnectionString("Accounts"));
        Assert.Null(configuration["AccountServer:GameLoginTickets:ActiveVerificationKeyId"]);
        Assert.False(configuration.GetSection("AccountServer:GameLoginTickets:VerificationKeys").Exists());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeploymentConfiguration_RejectsMissingAccountConnectionString(bool development)
    {
        Dictionary<string, string?> values = CreateProductionValues();
        values.Remove("ConnectionStrings:Accounts");

        IConfiguration configuration = LoadConfiguration(development, values);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.Equal("ConnectionStrings:Accounts is missing or empty.", exception.Message);
    }

    [Theory]
    [InlineData("AccountServer:Network:BindAddress")]
    [InlineData("AccountServer:Network:LoginPort")]
    [InlineData("AccountServer:Network:GameServerAddress")]
    [InlineData("AccountServer:Network:GameServerPort")]
    public void ProductionConfiguration_RejectsMissingNetworkSettings(string configurationKey)
    {
        Dictionary<string, string?> values = CreateProductionValues();
        values.Remove(configurationKey);

        IConfiguration configuration = LoadConfiguration(false, values);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeploymentConfiguration_RejectsMissingVerificationKeys(bool development)
    {
        Dictionary<string, string?> values = CreateProductionValues();

        values.Remove("AccountServer:GameLoginTickets:VerificationKeys:0:Id");
        values.Remove("AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey");

        IConfiguration configuration = LoadConfiguration(development, values);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeploymentConfiguration_RejectsMissingActiveVerificationKeyId(bool development)
    {
        Dictionary<string, string?> values = CreateProductionValues();
        values.Remove("AccountServer:GameLoginTickets:ActiveVerificationKeyId");

        IConfiguration configuration = LoadConfiguration(development, values);
        AccountServerConfiguration runtimeConfiguration = AccountServerConfiguration.Load(configuration);
        ServiceCollection services = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => AccountServerHost.ConfigureServices(services, runtimeConfiguration));
    }

    [Fact]
    public void DevelopmentConfiguration_LoadsWithExternallySuppliedSecrets()
    {
        IConfiguration configuration = LoadConfiguration(true, CreateSecrets());

        AccountServerConfiguration result = AccountServerConfiguration.Load(configuration);

        Assert.Equal(IPAddress.Loopback, result.LoginEndPoint.Address);
        Assert.Equal(9958, result.LoginEndPoint.Port);
        Assert.Equal("127.0.0.1", result.Handshake.GameServerIp);
        Assert.Equal(5816u, result.Handshake.GameServerPort);

        Assert.Equal(512, result.ListenBacklog);
        Assert.Equal(1024, result.AdmissionCapacity);
        Assert.Equal(6, result.LoginConnectionProtection.MaximumConcurrentConnectionsPerSource);

        Assert.Equal(8, result.WorkerPool.WorkerCount);
        Assert.Equal(TimeSpan.FromSeconds(15), result.WorkerPool.ConnectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Handshake.PhaseTimeout);

        Assert.Equal(30, result.AuthenticationProtection.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(1), result.AuthenticationProtection.RequestWindow);
        Assert.Equal(4, result.AuthenticationProtection.MaximumConcurrentRequestsPerSource);
        Assert.Equal(32, result.AuthenticationProtection.MaximumConcurrentRequests);
        Assert.Equal(2, result.AuthenticationProtection.MaximumConcurrentAttemptsPerAccount);
        Assert.Equal(8, result.AuthenticationProtection.FailedAttemptLimitPerAccountSource);
        Assert.Equal(TimeSpan.FromMinutes(5), result.AuthenticationProtection.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), result.AuthenticationProtection.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(10), result.AuthenticationProtection.EntryRetention);
        Assert.Equal(100000, result.AuthenticationProtection.MaximumTrackedEntries);

        Assert.Equal((ushort)1, result.ActiveVerificationKeyId);
        Assert.Single(result.EncodedVerificationKeys);
        Assert.Equal((ushort)1, result.EncodedVerificationKeys[0].Key);
        Assert.Equal(AccountServerTestConfiguration.VerificationKeyOne, result.EncodedVerificationKeys[0].Value);

        Assert.Equal(TimeSpan.FromMinutes(1), result.TicketCleanup.Interval);
        Assert.Equal(10, result.TicketCleanup.MaximumBatchesPerRun);
    }

    [Fact]
    public void ProductionConfiguration_LoadsWhenRequiredDeploymentValuesAreSupplied()
    {
        IConfiguration configuration = LoadConfiguration(false, CreateProductionValues());

        AccountServerConfiguration result = AccountServerConfiguration.Load(configuration);

        Assert.Equal(IPAddress.Loopback, result.LoginEndPoint.Address);
        Assert.Equal(9958, result.LoginEndPoint.Port);
        Assert.Equal("127.0.0.1", result.Handshake.GameServerIp);
        Assert.Equal(5816u, result.Handshake.GameServerPort);
    }

    [Fact]
    public void DeploymentConfiguration_AllowsExplicitOperationalOverrides()
    {
        Dictionary<string, string?> values = CreateSecrets();

        values["AccountServer:Workers:Count"] = "4";
        values["AccountServer:AuthenticationProtection:RequestLimitPerSource"] = "20";

        IConfiguration configuration = LoadConfiguration(true, values);

        AccountServerConfiguration result = AccountServerConfiguration.Load(configuration);

        Assert.Equal(4, result.WorkerPool.WorkerCount);
        Assert.Equal(20, result.AuthenticationProtection.RequestLimitPerSource);
    }

    private static IConfiguration LoadConfiguration(bool development, Dictionary<string, string?>? overrides = null)
    {
        ConfigurationBuilder builder = new();

        builder.SetBasePath(Path.Combine(AppContext.BaseDirectory, "AccountServerConfiguration"));
        builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        if (development)
        {
            builder.AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false);
        }

        if (overrides is not null)
        {
            builder.AddInMemoryCollection(overrides);
        }

        return builder.Build();
    }

    private static Dictionary<string, string?> CreateProductionValues()
    {
        Dictionary<string, string?> values = CreateSecrets();

        values["AccountServer:Network:BindAddress"] = "127.0.0.1";
        values["AccountServer:Network:LoginPort"] = "9958";
        values["AccountServer:Network:GameServerAddress"] = "127.0.0.1";
        values["AccountServer:Network:GameServerPort"] = "5816";

        return values;
    }

    private static Dictionary<string, string?> CreateSecrets()
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Accounts"] = "Server=localhost;Database=accounts",
            ["AccountServer:GameLoginTickets:ActiveVerificationKeyId"] = "1",
            ["AccountServer:GameLoginTickets:VerificationKeys:0:Id"] = "1",
            ["AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey"] = AccountServerTestConfiguration.VerificationKeyOne
        };
    }
}
