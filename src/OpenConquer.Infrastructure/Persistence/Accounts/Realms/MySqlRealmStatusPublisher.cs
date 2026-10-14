using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Realms;

internal sealed class MySqlRealmStatusPublisher(MySqlDataSource dataSource, RealmId realmId) : IRealmStatusPublisher
{
    private static readonly TimeSpan s_minimumLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_maximumLifetime = TimeSpan.FromHours(1);

    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly RealmId _realmId = realmId.Value != 0 ? realmId : throw new ArgumentOutOfRangeException(nameof(realmId));

    public async ValueTask PublishAsync(RealmLoad load, int onlineCount, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(load))
        {
            throw new ArgumentOutOfRangeException(nameof(load), load, "Unknown realm load.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(onlineCount);

        if (lifetime < s_minimumLifetime || lifetime > s_maximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), $"A realm status lifetime must be between {s_minimumLifetime} and {s_maximumLifetime}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();

        // Database time, as for login tickets, so that report expiry never depends on
        // clock agreement between the GameServer and AccountServer hosts.
        command.CommandText = """
            INSERT INTO `realm_status` (`realm_id`, `load_state`, `online_count`, `reported_at_utc`, `expires_at_utc`)
            VALUES (@realm_id, @load_state, @online_count, UTC_TIMESTAMP(6), TIMESTAMPADD(MICROSECOND, @lifetime_microseconds, UTC_TIMESTAMP(6))) AS `report`
            ON DUPLICATE KEY UPDATE
                `load_state` = `report`.`load_state`,
                `online_count` = `report`.`online_count`,
                `reported_at_utc` = `report`.`reported_at_utc`,
                `expires_at_utc` = `report`.`expires_at_utc`;
            """;
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;
        command.Parameters.Add("@load_state", MySqlDbType.UByte).Value = (byte)load;
        command.Parameters.Add("@online_count", MySqlDbType.UInt32).Value = (uint)onlineCount;
        command.Parameters.Add("@lifetime_microseconds", MySqlDbType.Int64).Value = lifetime.Ticks / TimeSpan.TicksPerMicrosecond;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WithdrawAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `realm_status` WHERE `realm_id` = @realm_id;";
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
