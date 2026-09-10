using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

namespace OpenConquer.AccountServer.Maintenance;

internal sealed partial class GameLoginTicketCleanupHostedService(IGameLoginTicketExpirationCleaner cleaner, GameLoginTicketCleanupConfiguration configuration, TimeProvider timeProvider, ILogger<GameLoginTicketCleanupHostedService> logger) : BackgroundService
{
    private readonly IGameLoginTicketExpirationCleaner _cleaner = cleaner ?? throw new ArgumentNullException(nameof(cleaner));
    private readonly GameLoginTicketCleanupConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<GameLoginTicketCleanupHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryDeleteExpiredTicketsAsync(stoppingToken).ConfigureAwait(false);

        using PeriodicTimer timer = new(_configuration.Interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TryDeleteExpiredTicketsAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask<int> DeleteExpiredTicketsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int totalDeleted = 0;

        for (int batch = 0; batch < _configuration.MaximumBatchesPerRun; batch++)
        {
            int deleted = await _cleaner.DeleteExpiredBatchAsync(cancellationToken).ConfigureAwait(false);

            if (deleted is < 0 || deleted > _cleaner.MaximumBatchSize)
            {
                throw new InvalidOperationException($"The game-login ticket expiration cleaner reported {deleted} deleted rows for a maximum batch size of {_cleaner.MaximumBatchSize}.");
            }

            totalDeleted = checked(totalDeleted + deleted);

            if (deleted < _cleaner.MaximumBatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    private async Task TryDeleteExpiredTicketsAsync(CancellationToken cancellationToken)
    {
        try
        {
            int deleted = await DeleteExpiredTicketsAsync(cancellationToken).ConfigureAwait(false);

            if (deleted == 0)
            {
                LogCleanupCompletedWithoutDeletes(_logger);
            }
            else
            {
                LogCleanupCompleted(_logger, deleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCleanupFailed(_logger, exception);
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Deleted {DeletedCount} expired game-login tickets.")]
    private static partial void LogCleanupCompleted(ILogger logger, int deletedCount);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug, Message = "Game-login ticket cleanup completed with no expired tickets.")]
    private static partial void LogCleanupCompletedWithoutDeletes(ILogger logger);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "Game-login ticket cleanup failed; the next scheduled run will retry.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
