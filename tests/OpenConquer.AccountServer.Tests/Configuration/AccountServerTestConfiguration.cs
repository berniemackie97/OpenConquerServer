using Microsoft.Extensions.Configuration;

namespace OpenConquer.AccountServer.Tests.Configuration;

internal static class AccountServerTestConfiguration
{
    public const string VerificationKeyOne = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";
    public const string VerificationKeyTwo = "AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=";

    public static IConfiguration Create(Action<Dictionary<string, string?>>? configure = null)
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
