using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.AccountServer.Maintenance;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.AccountServer.Configuration;

internal sealed class AccountServerConfiguration
{
    public const string SectionName = "AccountServer";

    private AccountServerConfiguration(string accountConnectionString, IPEndPoint loginEndPoint, int listenBacklog, int admissionCapacity, LoginConnectionWorkerPoolConfiguration workerPool, LoginHandshakeConfiguration handshake, AccountAuthenticationProtectionOptions authenticationProtection, ushort activeVerificationKeyId, KeyValuePair<ushort, string>[] encodedVerificationKeys, GameLoginTicketCleanupConfiguration ticketCleanup)
    {
        AccountConnectionString = accountConnectionString;
        LoginEndPoint = loginEndPoint;
        ListenBacklog = listenBacklog;
        AdmissionCapacity = admissionCapacity;
        WorkerPool = workerPool;
        Handshake = handshake;
        AuthenticationProtection = authenticationProtection;
        ActiveVerificationKeyId = activeVerificationKeyId;
        EncodedVerificationKeys = encodedVerificationKeys;
        TicketCleanup = ticketCleanup;
    }

    public string AccountConnectionString { get; }
    public IPEndPoint LoginEndPoint { get; }
    public int ListenBacklog { get; }
    public int AdmissionCapacity { get; }
    public LoginConnectionWorkerPoolConfiguration WorkerPool { get; }
    public LoginHandshakeConfiguration Handshake { get; }
    public AccountAuthenticationProtectionOptions AuthenticationProtection { get; }
    public ushort ActiveVerificationKeyId { get; }
    public IReadOnlyList<KeyValuePair<ushort, string>> EncodedVerificationKeys { get; }
    public GameLoginTicketCleanupConfiguration TicketCleanup { get; }

    public static AccountServerConfiguration Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string accountConnectionString = configuration.GetConnectionString("Accounts") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(accountConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Accounts is missing or empty.");
        }

        AccountServerSettings settings = new();

        configuration.GetRequiredSection(SectionName).Bind(settings, options => options.ErrorOnUnknownConfiguration = true);

        try
        {
            IPAddress bindAddress = ParseRequiredIpv4(settings.Network.BindAddress, $"{SectionName}:Network:BindAddress");
            IPAddress gameServerAddress = ParseRequiredIpv4(settings.Network.GameServerAddress, $"{SectionName}:Network:GameServerAddress");

            int loginPort = ValidatePort(settings.Network.LoginPort, $"{SectionName}:Network:LoginPort");
            int listenBacklog = ValidatePositive(settings.Network.ListenBacklog, $"{SectionName}:Network:ListenBacklog");
            int admissionCapacity = ValidatePositive(settings.Admission.Capacity, $"{SectionName}:Admission:Capacity");

            LoginConnectionWorkerPoolConfiguration workerPool = new(settings.Workers.Count, settings.Workers.ConnectionTimeout);
            LoginHandshakeConfiguration handshake = new(gameServerAddress, settings.Network.GameServerPort, settings.Handshake.PhaseTimeout);

            AccountAuthenticationProtectionOptions authenticationProtection = new(
                settings.AuthenticationProtection.RequestLimitPerSource,
                settings.AuthenticationProtection.RequestWindow,
                settings.AuthenticationProtection.MaximumConcurrentRequestsPerSource,
                settings.AuthenticationProtection.MaximumConcurrentRequests,
                settings.AuthenticationProtection.MaximumConcurrentAttemptsPerAccount,
                settings.AuthenticationProtection.FailedAttemptLimitPerAccountSource,
                settings.AuthenticationProtection.FailureWindow,
                settings.AuthenticationProtection.FailureLockout,
                settings.AuthenticationProtection.EntryRetention,
                settings.AuthenticationProtection.MaximumTrackedEntries);

            KeyValuePair<ushort, string>[] encodedVerificationKeys = CreateVerificationKeys(settings.GameLoginTickets.VerificationKeys);

            GameLoginTicketCleanupConfiguration ticketCleanup = new(settings.GameLoginTickets.Cleanup.Interval, settings.GameLoginTickets.Cleanup.MaximumBatchesPerRun);

            return new AccountServerConfiguration(accountConnectionString, new IPEndPoint(bindAddress, loginPort), listenBacklog, admissionCapacity, workerPool, handshake, authenticationProtection, settings.GameLoginTickets.ActiveVerificationKeyId, encodedVerificationKeys, ticketCleanup);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidOperationException($"Configuration section '{SectionName}' is invalid.", exception);
        }
    }

    private static IPAddress ParseRequiredIpv4(string? value, string configurationPath)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"{configurationPath} must contain a valid IPv4 address.", nameof(value));
        }

        return address;
    }

    private static int ValidatePort(int value, string configurationPath)
    {
        if (value is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{configurationPath} must be between 1 and {IPEndPoint.MaxPort}.");
        }

        return value;
    }

    private static int ValidatePositive(int value, string configurationPath)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{configurationPath} must be greater than zero.");
        }

        return value;
    }

    private static KeyValuePair<ushort, string>[] CreateVerificationKeys(IReadOnlyCollection<GameLoginVerificationKeySettings> settings)
    {
        if (settings.Count == 0)
        {
            throw new ArgumentException($"{SectionName}:GameLoginTickets:VerificationKeys must contain at least one verification key.", nameof(settings));
        }

        KeyValuePair<ushort, string>[] keys = new KeyValuePair<ushort, string>[settings.Count];
        int index = 0;

        foreach (GameLoginVerificationKeySettings key in settings)
        {
            if (string.IsNullOrWhiteSpace(key.EncodedKey))
            {
                throw new ArgumentException($"{SectionName}:GameLoginTickets:VerificationKeys contains missing key material.", nameof(settings));
            }

            keys[index++] = new KeyValuePair<ushort, string>(key.Id, key.EncodedKey);
        }

        return keys;
    }

    private sealed class AccountServerSettings
    {
        public NetworkSettings Network { get; set; } = new();
        public AdmissionSettings Admission { get; set; } = new();
        public WorkerSettings Workers { get; set; } = new();
        public HandshakeSettings Handshake { get; set; } = new();
        public AuthenticationProtectionSettings AuthenticationProtection { get; set; } = new();
        public GameLoginTicketSettings GameLoginTickets { get; set; } = new();
    }

    private sealed class NetworkSettings
    {
        public string? BindAddress { get; set; }
        public int LoginPort { get; set; }
        public int ListenBacklog { get; set; }
        public string? GameServerAddress { get; set; }
        public int GameServerPort { get; set; }
    }

    private sealed class AdmissionSettings
    {
        public int Capacity { get; set; }
    }

    private sealed class WorkerSettings
    {
        public int Count { get; set; }
        public TimeSpan ConnectionTimeout { get; set; }
    }

    private sealed class HandshakeSettings
    {
        public TimeSpan PhaseTimeout { get; set; }
    }

    private sealed class AuthenticationProtectionSettings
    {
        public int RequestLimitPerSource { get; set; }
        public TimeSpan RequestWindow { get; set; }
        public int MaximumConcurrentRequestsPerSource { get; set; }
        public int MaximumConcurrentRequests { get; set; }
        public int MaximumConcurrentAttemptsPerAccount { get; set; }
        public int FailedAttemptLimitPerAccountSource { get; set; }
        public TimeSpan FailureWindow { get; set; }
        public TimeSpan FailureLockout { get; set; }
        public TimeSpan EntryRetention { get; set; }
        public int MaximumTrackedEntries { get; set; }
    }

    private sealed class GameLoginTicketSettings
    {
        public ushort ActiveVerificationKeyId { get; set; }
        public List<GameLoginVerificationKeySettings> VerificationKeys { get; set; } = [];
        public GameLoginTicketCleanupSettings Cleanup { get; set; } = new();
    }

    private sealed class GameLoginVerificationKeySettings
    {
        public ushort Id { get; set; }
        public string? EncodedKey { get; set; }
    }

    private sealed class GameLoginTicketCleanupSettings
    {
        public TimeSpan Interval { get; set; }
        public int MaximumBatchesPerRun { get; set; }
    }
}
