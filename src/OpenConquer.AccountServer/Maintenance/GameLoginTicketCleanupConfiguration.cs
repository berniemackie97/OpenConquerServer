namespace OpenConquer.AccountServer.Maintenance;

internal sealed class GameLoginTicketCleanupConfiguration
{
    private static readonly TimeSpan s_minimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maximumInterval = TimeSpan.FromDays(1);

    public GameLoginTicketCleanupConfiguration(TimeSpan interval, int maximumBatchesPerRun)
    {
        if (interval < s_minimumInterval || interval > s_maximumInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), $"The game-login ticket cleanup interval must be between {s_minimumInterval} and {s_maximumInterval}.");
        }

        if (maximumBatchesPerRun is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBatchesPerRun), "The maximum cleanup batches per run must be between 1 and 1,000.");
        }

        Interval = interval;
        MaximumBatchesPerRun = maximumBatchesPerRun;
    }

    public TimeSpan Interval { get; }
    public int MaximumBatchesPerRun { get; }
}
