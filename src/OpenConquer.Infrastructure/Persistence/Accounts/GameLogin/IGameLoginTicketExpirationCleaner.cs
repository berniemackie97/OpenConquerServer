namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

public interface IGameLoginTicketExpirationCleaner
{
    int MaximumBatchSize { get; }

    ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default);
}
