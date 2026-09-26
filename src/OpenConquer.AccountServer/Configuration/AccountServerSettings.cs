using OpenConquer.Infrastructure.Security.Accounts.Authentication;

namespace OpenConquer.AccountServer.Configuration;

internal sealed class AccountServerSettings
{
    public NetworkSettings Network { get; set; } = new();
    public AdmissionSettings Admission { get; set; } = new();
    public WorkerSettings Workers { get; set; } = new();
    public HandshakeSettings Handshake { get; set; } = new();
    public AuthenticationProtectionSettings AuthenticationProtection { get; set; } = new();
    public GameLoginTicketSettings GameLoginTickets { get; set; } = new();

    internal sealed class NetworkSettings
    {
        public string? BindAddress { get; set; }
        public int LoginPort { get; set; }
        public int ListenBacklog { get; set; }
        public string? GameServerAddress { get; set; }
        public int GameServerPort { get; set; }
    }

    internal sealed class AdmissionSettings
    {
        public int Capacity { get; set; }
        public int MaximumConcurrentConnectionsPerSource { get; set; } = AccountLoginConnectionProtectionOptions.DefaultMaximumConcurrentConnectionsPerSource;
    }

    internal sealed class WorkerSettings
    {
        public int Count { get; set; }
        public TimeSpan ConnectionTimeout { get; set; }
    }

    internal sealed class HandshakeSettings
    {
        public TimeSpan PhaseTimeout { get; set; }
    }

    internal sealed class AuthenticationProtectionSettings
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

    internal sealed class GameLoginTicketSettings
    {
        public ushort ActiveVerificationKeyId { get; set; }
        public List<GameLoginVerificationKeySettings> VerificationKeys { get; set; } = [];
        public GameLoginTicketCleanupSettings Cleanup { get; set; } = new();
    }

    internal sealed class GameLoginVerificationKeySettings
    {
        public ushort Id { get; set; }
        public string? EncodedKey { get; set; }
    }

    internal sealed class GameLoginTicketCleanupSettings
    {
        public TimeSpan Interval { get; set; }
        public int MaximumBatchesPerRun { get; set; }
    }
}
