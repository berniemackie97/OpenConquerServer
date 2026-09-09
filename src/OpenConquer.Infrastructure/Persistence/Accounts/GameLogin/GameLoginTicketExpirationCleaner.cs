using MySqlConnector;

namespace OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

internal sealed class GameLoginTicketExpirationCleaner(MySqlDataSource dataSource, GameLoginTicketExpirationCleanerOptions options)
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly GameLoginTicketExpirationCleanerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long expirationGraceMicroseconds = GetExpirationGraceMicroseconds(_options.ExpirationGrace);

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM `game_login_tickets`
            WHERE `expires_at_utc` <= TIMESTAMPADD(
                MICROSECOND,
                @expiration_grace_offset_microseconds,
                UTC_TIMESTAMP(6))
            ORDER BY `expires_at_utc`, `session_uid`
            LIMIT @maximum_batch_size;
            """;

        command.Parameters.Add("@expiration_grace_offset_microseconds", MySqlDbType.Int64).Value = -expirationGraceMicroseconds;
        command.Parameters.Add("@maximum_batch_size", MySqlDbType.Int32).Value = _options.MaximumBatchSize;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected < 0 || affected > _options.MaximumBatchSize)
        {
            throw new InvalidOperationException($"Game-login ticket expiration cleanup affected an unexpected number of rows. Expected between 0 and {_options.MaximumBatchSize}, but MySQL reported {affected}.");
        }

        return affected;
    }

    internal static long GetExpirationGraceMicroseconds(TimeSpan expirationGrace)
    {
        return checked((expirationGrace.Ticks + TimeSpan.TicksPerMicrosecond - 1) / TimeSpan.TicksPerMicrosecond);
    }
}
