namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

public sealed class AccountAuthenticationProtectionOptions
{
    public const int DefaultRequestLimitPerSource = 30;
    public const int DefaultMaximumConcurrentRequestsPerSource = 4;
    public const int DefaultMaximumConcurrentRequests = 32;
    public const int DefaultMaximumConcurrentAttemptsPerAccount = 2;
    public const int DefaultFailedAttemptLimitPerAccountSource = 8;
    public const int DefaultMaximumTrackedEntries = 100_000;

    public static readonly TimeSpan DefaultRequestWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultFailureWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultFailureLockout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultEntryRetention = TimeSpan.FromMinutes(10);

    public AccountAuthenticationProtectionOptions(int requestLimitPerSource = DefaultRequestLimitPerSource, TimeSpan? requestWindow = null, int maximumConcurrentRequestsPerSource = DefaultMaximumConcurrentRequestsPerSource,
        int maximumConcurrentRequests = DefaultMaximumConcurrentRequests, int maximumConcurrentAttemptsPerAccount = DefaultMaximumConcurrentAttemptsPerAccount,
        int failedAttemptLimitPerAccountSource = DefaultFailedAttemptLimitPerAccountSource, TimeSpan? failureWindow = null, TimeSpan? failureLockout = null,
        TimeSpan? entryRetention = null, int maximumTrackedEntries = DefaultMaximumTrackedEntries)
    {
        RequestLimitPerSource = ValidateLimit(requestLimitPerSource, nameof(requestLimitPerSource), 1_000_000);
        RequestWindow = ValidateWindow(requestWindow ?? DefaultRequestWindow, nameof(requestWindow), TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));
        MaximumConcurrentRequestsPerSource = ValidateLimit(maximumConcurrentRequestsPerSource, nameof(maximumConcurrentRequestsPerSource), 1_024);
        MaximumConcurrentRequests = ValidateLimit(maximumConcurrentRequests, nameof(maximumConcurrentRequests), 1_000_000);
        MaximumConcurrentAttemptsPerAccount = ValidateLimit(maximumConcurrentAttemptsPerAccount, nameof(maximumConcurrentAttemptsPerAccount), 1_024);
        FailedAttemptLimitPerAccountSource = ValidateLimit(failedAttemptLimitPerAccountSource, nameof(failedAttemptLimitPerAccountSource), 1_000_000);
        FailureWindow = ValidateWindow(failureWindow ?? DefaultFailureWindow, nameof(failureWindow), TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));
        FailureLockout = ValidateWindow(failureLockout ?? DefaultFailureLockout, nameof(failureLockout), TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));

        TimeSpan minimumRetention = RequestWindow > FailureWindow ? RequestWindow : FailureWindow;

        if (FailureLockout > minimumRetention)
        {
            minimumRetention = FailureLockout;
        }

        EntryRetention = ValidateWindow(entryRetention ?? DefaultEntryRetention, nameof(entryRetention), minimumRetention, TimeSpan.FromDays(7));
        MaximumTrackedEntries = ValidateLimit(maximumTrackedEntries, nameof(maximumTrackedEntries), 2_000_000);

        if (MaximumConcurrentRequestsPerSource > MaximumConcurrentRequests)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentRequestsPerSource), "The per-source concurrency limit cannot exceed the global request concurrency limit.");
        }

        if (MaximumConcurrentAttemptsPerAccount > MaximumConcurrentRequests)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentAttemptsPerAccount), "The per-account concurrency limit cannot exceed the global request concurrency limit.");
        }

        if (MaximumTrackedEntries < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTrackedEntries), "Authentication protection requires capacity for at least one source, one account, and one account-source entry.");
        }
    }

    public int RequestLimitPerSource { get; }
    public TimeSpan RequestWindow { get; }
    public int MaximumConcurrentRequestsPerSource { get; }
    public int MaximumConcurrentRequests { get; }
    public int MaximumConcurrentAttemptsPerAccount { get; }
    public int FailedAttemptLimitPerAccountSource { get; }
    public TimeSpan FailureWindow { get; }
    public TimeSpan FailureLockout { get; }
    public TimeSpan EntryRetention { get; }
    public int MaximumTrackedEntries { get; }

    private static int ValidateLimit(int value, string parameterName, int maximum)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"The value must be between 1 and {maximum}.");
        }

        return value;
    }

    private static TimeSpan ValidateWindow(TimeSpan value, string parameterName, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"The value must be between {minimum} and {maximum}.");
        }

        return value;
    }
}
