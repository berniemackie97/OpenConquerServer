namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketExpirationCleanerOptions
{
    public const int DefaultMaximumBatchSize = 1_000;

    public static readonly TimeSpan DefaultExpirationGrace = TimeSpan.FromMinutes(5);

    public GameLoginTicketExpirationCleanerOptions(TimeSpan? expirationGrace = null, int maximumBatchSize = DefaultMaximumBatchSize)
    {
        ExpirationGrace = expirationGrace ?? DefaultExpirationGrace;
        MaximumBatchSize = maximumBatchSize;

        if (ExpirationGrace <= TimeSpan.Zero || ExpirationGrace > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(expirationGrace), "The expiration grace must be greater than zero and no greater than one day.");
        }

        if (MaximumBatchSize < 1 || MaximumBatchSize > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize), "The maximum cleanup batch size must be between 1 and 10,000 rows.");
        }
    }

    public TimeSpan ExpirationGrace { get; }
    public int MaximumBatchSize { get; }
}
