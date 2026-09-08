namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

internal sealed class GameLoginTicketRedemptionAttemptLimiterOptions
{
    public const int DefaultRequestLimitPerSource = 30;
    public const int DefaultMaximumConcurrentAttemptsPerSource = 4;
    public const int DefaultMaximumConcurrentAttemptsPerSession = 1;
    public const int DefaultFailedAttemptLimitPerSession = 8;
    public const int DefaultMaximumConcurrentAttempts = 512;
    public const int DefaultMaximumTrackedEntries = 100_000;

    public static readonly TimeSpan DefaultRequestWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultFailureWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultFailureLockout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultEntryRetention = TimeSpan.FromMinutes(10);

    public GameLoginTicketRedemptionAttemptLimiterOptions(int requestLimitPerSource = DefaultRequestLimitPerSource, TimeSpan? requestWindow = null, int maximumConcurrentAttemptsPerSource = DefaultMaximumConcurrentAttemptsPerSource,
        int maximumConcurrentAttemptsPerSession = DefaultMaximumConcurrentAttemptsPerSession, int failedAttemptLimitPerSession = DefaultFailedAttemptLimitPerSession,
        TimeSpan? failureWindow = null, TimeSpan? failureLockout = null, TimeSpan? entryRetention = null, int maximumConcurrentAttempts = DefaultMaximumConcurrentAttempts,
        int maximumTrackedEntries = DefaultMaximumTrackedEntries)
    {
        RequestLimitPerSource = ValidateLimit(requestLimitPerSource, nameof(requestLimitPerSource), 1_000_000);
        RequestWindow = ValidateWindow(requestWindow ?? DefaultRequestWindow, nameof(requestWindow), TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));
        MaximumConcurrentAttemptsPerSource = ValidateLimit(maximumConcurrentAttemptsPerSource, nameof(maximumConcurrentAttemptsPerSource), 1_024);
        MaximumConcurrentAttemptsPerSession = ValidateLimit(maximumConcurrentAttemptsPerSession, nameof(maximumConcurrentAttemptsPerSession), 1_024);
        FailedAttemptLimitPerSession = ValidateLimit(failedAttemptLimitPerSession, nameof(failedAttemptLimitPerSession), 1_000_000);
        FailureWindow = ValidateWindow(failureWindow ?? DefaultFailureWindow, nameof(failureWindow), TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));
        FailureLockout = ValidateWindow(failureLockout ?? DefaultFailureLockout, nameof(failureLockout), TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));
        MaximumConcurrentAttempts = ValidateLimit(maximumConcurrentAttempts, nameof(maximumConcurrentAttempts), 1_000_000);
        MaximumTrackedEntries = ValidateLimit(maximumTrackedEntries, nameof(maximumTrackedEntries), 2_000_000);

        TimeSpan minimumRetention = RequestWindow > FailureWindow ? RequestWindow : FailureWindow;

        if (FailureLockout > minimumRetention)
        {
            minimumRetention = FailureLockout;
        }

        EntryRetention = ValidateWindow(entryRetention ?? DefaultEntryRetention, nameof(entryRetention), minimumRetention, TimeSpan.FromDays(7));

        if (MaximumConcurrentAttemptsPerSource > MaximumConcurrentAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentAttemptsPerSource), "The per-source concurrency limit cannot exceed the global concurrency limit.");
        }

        if (MaximumConcurrentAttemptsPerSession > MaximumConcurrentAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentAttemptsPerSession), "The per-session concurrency limit cannot exceed the global concurrency limit.");
        }

        if (MaximumTrackedEntries < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTrackedEntries), "Game-login redemption protection requires capacity for at least one source and one session entry.");
        }
    }

    public int RequestLimitPerSource { get; }
    public TimeSpan RequestWindow { get; }
    public int MaximumConcurrentAttemptsPerSource { get; }
    public int MaximumConcurrentAttemptsPerSession { get; }
    public int FailedAttemptLimitPerSession { get; }
    public TimeSpan FailureWindow { get; }
    public TimeSpan FailureLockout { get; }
    public TimeSpan EntryRetention { get; }
    public int MaximumConcurrentAttempts { get; }
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
