using System.Net;
using Microsoft.Extensions.Configuration;
using OpenConquer.AccountServer.Configuration;

namespace OpenConquer.AccountServer.Tests.Configuration;

public sealed class AccountServerConfigurationTests
{
    private const string VerificationKeyOne = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";
    private const string VerificationKeyTwo = "AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=";

    [Fact]
    public void Load_RejectsMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => AccountServerConfiguration.Load(null!));
    }

    [Fact]
    public void Load_RejectsMissingAccountConnectionString()
    {
        IConfiguration configuration = CreateConfiguration(values => values.Remove("ConnectionStrings:Accounts"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.Equal("ConnectionStrings:Accounts is missing or empty.", exception.Message);
    }

    [Fact]
    public void Load_RejectsMissingAccountServerSection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Accounts"] = "Server=localhost;Database=accounts",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));
    }

    [Fact]
    public void Load_RejectsUnknownAccountServerSetting()
    {
        IConfiguration configuration = CreateConfiguration(values => values["AccountServer:Network:DeprecatedPort"] = "1234");

        Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));
    }

    [Theory]
    [InlineData("AccountServer:Network:BindAddress")]
    [InlineData("AccountServer:Network:GameServerAddress")]
    public void Load_RejectsIpv6Endpoints(string configurationKey)
    {
        IConfiguration configuration = CreateConfiguration(values => values[configurationKey] = "::1");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Load_RejectsInvalidRuntimeConfiguration()
    {
        IConfiguration configuration = CreateConfiguration(values => values["AccountServer:Workers:Count"] = "0");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void Load_RejectsMissingVerificationKeys()
    {
        IConfiguration configuration = CreateConfiguration(values =>
        {
            values.Remove("AccountServer:GameLoginTickets:VerificationKeys:0:Id");
            values.Remove("AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey");
            values.Remove("AccountServer:GameLoginTickets:VerificationKeys:1:Id");
            values.Remove("AccountServer:GameLoginTickets:VerificationKeys:1:EncodedKey");
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AccountServerConfiguration.Load(configuration));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Load_PreservesVerificationKeyEntriesForKeyRingValidation()
    {
        IConfiguration configuration = CreateConfiguration(values => values["AccountServer:GameLoginTickets:VerificationKeys:1:Id"] = "7");

        AccountServerConfiguration result = AccountServerConfiguration.Load(configuration);

        Assert.Collection(
            result.EncodedVerificationKeys,
            key => Assert.Equal((ushort)7, key.Key),
            key => Assert.Equal((ushort)7, key.Key));
    }

    [Fact]
    public void Load_ProjectsDeploymentConfigurationIntoValidatedRuntimeConfiguration()
    {
        IConfiguration configuration = CreateConfiguration();

        AccountServerConfiguration result = AccountServerConfiguration.Load(configuration);

        Assert.Equal("Server=localhost;Database=accounts", result.AccountConnectionString);

        Assert.Equal(IPAddress.Loopback, result.LoginEndPoint.Address);
        Assert.Equal(9958, result.LoginEndPoint.Port);
        Assert.Equal(512, result.ListenBacklog);
        Assert.Equal(1_024, result.AdmissionCapacity);

        Assert.Equal(8, result.WorkerPool.WorkerCount);
        Assert.Equal(TimeSpan.FromSeconds(15), result.WorkerPool.ConnectionTimeout);

        Assert.Equal("127.0.0.1", result.Handshake.GameServerIp);
        Assert.Equal(5816u, result.Handshake.GameServerPort);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Handshake.PhaseTimeout);

        Assert.Equal(60, result.AuthenticationProtection.RequestLimitPerSource);
        Assert.Equal(TimeSpan.FromMinutes(2), result.AuthenticationProtection.RequestWindow);
        Assert.Equal(6, result.AuthenticationProtection.MaximumConcurrentRequestsPerSource);
        Assert.Equal(64, result.AuthenticationProtection.MaximumConcurrentRequests);
        Assert.Equal(3, result.AuthenticationProtection.MaximumConcurrentAttemptsPerAccount);
        Assert.Equal(10, result.AuthenticationProtection.FailedAttemptLimitPerAccountSource);
        Assert.Equal(TimeSpan.FromMinutes(6), result.AuthenticationProtection.FailureWindow);
        Assert.Equal(TimeSpan.FromMinutes(7), result.AuthenticationProtection.FailureLockout);
        Assert.Equal(TimeSpan.FromMinutes(15), result.AuthenticationProtection.EntryRetention);
        Assert.Equal(120_000, result.AuthenticationProtection.MaximumTrackedEntries);

        Assert.Equal((ushort)7, result.ActiveVerificationKeyId);
        Assert.Collection(
            result.EncodedVerificationKeys,
            key =>
            {
                Assert.Equal((ushort)7, key.Key);
                Assert.Equal(VerificationKeyOne, key.Value);
            },
            key =>
            {
                Assert.Equal((ushort)9, key.Key);
                Assert.Equal(VerificationKeyTwo, key.Value);
            });

        Assert.Equal(TimeSpan.FromMinutes(1), result.TicketCleanup.Interval);
        Assert.Equal(10, result.TicketCleanup.MaximumBatchesPerRun);
    }

    private static IConfiguration CreateConfiguration(Action<Dictionary<string, string?>>? configure = null)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Accounts"] = "Server=localhost;Database=accounts",

            ["AccountServer:Network:BindAddress"] = "127.0.0.1",
            ["AccountServer:Network:LoginPort"] = "9958",
            ["AccountServer:Network:ListenBacklog"] = "512",
            ["AccountServer:Network:GameServerAddress"] = "127.0.0.1",
            ["AccountServer:Network:GameServerPort"] = "5816",

            ["AccountServer:Admission:Capacity"] = "1024",

            ["AccountServer:Workers:Count"] = "8",
            ["AccountServer:Workers:ConnectionTimeout"] = "00:00:15",

            ["AccountServer:Handshake:PhaseTimeout"] = "00:00:05",

            ["AccountServer:AuthenticationProtection:RequestLimitPerSource"] = "60",
            ["AccountServer:AuthenticationProtection:RequestWindow"] = "00:02:00",
            ["AccountServer:AuthenticationProtection:MaximumConcurrentRequestsPerSource"] = "6",
            ["AccountServer:AuthenticationProtection:MaximumConcurrentRequests"] = "64",
            ["AccountServer:AuthenticationProtection:MaximumConcurrentAttemptsPerAccount"] = "3",
            ["AccountServer:AuthenticationProtection:FailedAttemptLimitPerAccountSource"] = "10",
            ["AccountServer:AuthenticationProtection:FailureWindow"] = "00:06:00",
            ["AccountServer:AuthenticationProtection:FailureLockout"] = "00:07:00",
            ["AccountServer:AuthenticationProtection:EntryRetention"] = "00:15:00",
            ["AccountServer:AuthenticationProtection:MaximumTrackedEntries"] = "120000",

            ["AccountServer:GameLoginTickets:ActiveVerificationKeyId"] = "7",
            ["AccountServer:GameLoginTickets:VerificationKeys:0:Id"] = "7",
            ["AccountServer:GameLoginTickets:VerificationKeys:0:EncodedKey"] = VerificationKeyOne,
            ["AccountServer:GameLoginTickets:VerificationKeys:1:Id"] = "9",
            ["AccountServer:GameLoginTickets:VerificationKeys:1:EncodedKey"] = VerificationKeyTwo,
            ["AccountServer:GameLoginTickets:Cleanup:Interval"] = "00:01:00",
            ["AccountServer:GameLoginTickets:Cleanup:MaximumBatchesPerRun"] = "10",
        };

        configure?.Invoke(values);

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
