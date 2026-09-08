using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketExpirationCleaner(MySqlDataSource dataSource, GameLoginTicketExpirationCleanerOptions options, TimeProvider timeProvider)
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly GameLoginTicketExpirationCleanerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset cutoffUtc = _timeProvider.GetUtcNow() - _options.ExpirationGrace;

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM `game_login_tickets`
            WHERE `expires_at_utc` <= @cutoff_utc
            ORDER BY `expires_at_utc`, `session_uid`
            LIMIT @maximum_batch_size;
            """;

        command.Parameters.Add("@cutoff_utc", MySqlDbType.DateTime).Value = cutoffUtc.UtcDateTime;
        command.Parameters.Add("@maximum_batch_size", MySqlDbType.Int32).Value = _options.MaximumBatchSize;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected < 0 || affected > _options.MaximumBatchSize)
        {
            throw new InvalidOperationException($"Game-login ticket expiration cleanup affected an unexpected number of rows. Expected between 0 and {_options.MaximumBatchSize}, but MySQL reported {affected}.");
        }

        return affected;
    }
}
