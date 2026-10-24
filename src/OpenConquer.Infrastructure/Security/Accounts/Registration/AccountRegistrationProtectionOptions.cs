namespace OpenConquer.Infrastructure.Security.Accounts.Registration;

public sealed class AccountRegistrationProtectionOptions
{
    public const int DefaultAttemptLimitPerSource = 3;
    public static readonly TimeSpan DefaultAttemptWindow = TimeSpan.FromHours(1);
    public const int DefaultMaximumConcurrentAttempts = 2;
    public const int DefaultMaximumTrackedSources = 10_000;

    public AccountRegistrationProtectionOptions(int attemptLimitPerSource, TimeSpan attemptWindow,
        int maximumConcurrentAttempts, int maximumTrackedSources)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptLimitPerSource, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(attemptWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrentAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTrackedSources, 1);

        AttemptLimitPerSource = attemptLimitPerSource;
        AttemptWindow = attemptWindow;
        MaximumConcurrentAttempts = maximumConcurrentAttempts;
        MaximumTrackedSources = maximumTrackedSources;
    }

    /// <summary>Registration attempts one source (IPv4 address or IPv6 /64) may start per window.</summary>
    public int AttemptLimitPerSource { get; }

    public TimeSpan AttemptWindow { get; }

    /// <summary>Registrations hashing a password or writing at once, across all sources.</summary>
    public int MaximumConcurrentAttempts { get; }

    /// <summary>Sources tracked before new sources are refused until their windows expire.</summary>
    public int MaximumTrackedSources { get; }
}
