using MySqlConnector;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Sessions;

internal sealed class MySqlAccountSessionKickRequester(MySqlDataSource dataSource) : IAccountSessionKickRequester
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<bool> TryKickFromOtherRealmAsync(uint accountId, RealmId selectedRealm, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(accountId);

        if (selectedRealm.Value == 0)
        {
            throw new ArgumentException("A kick request requires a nonzero selected realm.", nameof(selectedRealm));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // A session in a realm that stopped reporting status is left behind by a GameServer that is gone, and
        // does not keep the account out of other realms.
        uint? activeRealm;

        await using (MySqlCommand find = connection.CreateCommand())
        {
            find.CommandText = """
                SELECT `session`.`realm_id`
                FROM `account_sessions` AS `session`
                JOIN `realm_status` AS `status`
                    ON `status`.`realm_id` = `session`.`realm_id`
                   AND `status`.`expires_at_utc` > UTC_TIMESTAMP(6)
                WHERE `session`.`account_id` = @account_id
                  AND `session`.`realm_id` <> @selected_realm_id;
                """;
            find.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
            find.Parameters.Add("@selected_realm_id", MySqlDbType.UInt32).Value = selectedRealm.Value;

            activeRealm = await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is uint realm ? realm : null;
        }

        if (activeRealm is not { } realmId)
        {
            return false;
        }

        await using MySqlCommand request = connection.CreateCommand();
        request.CommandText = AccountSessionKickSql.Request;
        request.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = realmId;
        request.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        await request.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
