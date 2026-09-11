namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

public sealed class AccountLoginConnectionProtectionOptions
{
    public const int DefaultMaximumConcurrentConnectionsPerSource = 4;

    public AccountLoginConnectionProtectionOptions(int maximumConcurrentConnectionsPerSource = DefaultMaximumConcurrentConnectionsPerSource)
    {
        if (maximumConcurrentConnectionsPerSource is < 1 or > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentConnectionsPerSource), "The per-source login-connection concurrency limit must be between 1 and 1024.");
        }

        MaximumConcurrentConnectionsPerSource = maximumConcurrentConnectionsPerSource;
    }

    public int MaximumConcurrentConnectionsPerSource { get; }
}
