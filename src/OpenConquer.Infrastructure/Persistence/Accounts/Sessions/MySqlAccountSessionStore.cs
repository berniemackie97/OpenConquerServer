using System.Data;
using MySqlConnector;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Domain.Realms;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Sessions;

internal sealed class MySqlAccountSessionStore(MySqlDataSource dataSource, RealmId realmId) : IAccountSessionStore
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    private readonly RealmId _realmId = realmId.Value != 0 ? realmId : throw new ArgumentOutOfRangeException(nameof(realmId));

    public async ValueTask<bool> TryEnterAsync(uint accountId, uint characterId, uint sessionUid, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(accountId);
        ArgumentOutOfRangeException.ThrowIfZero(characterId);
        ArgumentOutOfRangeException.ThrowIfZero(sessionUid);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        // Creates the row when the account has none and, either way, holds its lock until commit. That serializes
        // concurrent logins of one account across every realm.
        await using (MySqlCommand insert = CreateCommand(connection, transaction, """
            INSERT INTO `account_sessions` (`account_id`, `realm_id`, `character_id`, `session_uid`, `entered_at_utc`)
            VALUES (@account_id, @realm_id, @character_id, @session_uid, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE `account_id` = `account_id`;
            """))
        {
            AddSessionParameters(insert, accountId, characterId, sessionUid);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        Holder holder = await ReadHolderAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (holder.RealmId == _realmId.Value && holder.SessionUid == sessionUid)
        {
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }

        if (holder.RealmId == _realmId.Value || !holder.IsRealmReporting)
        {
            await using MySqlCommand update = CreateCommand(connection, transaction, """
                UPDATE `account_sessions`
                SET `realm_id` = @realm_id,
                    `character_id` = @character_id,
                    `session_uid` = @session_uid,
                    `entered_at_utc` = UTC_TIMESTAMP(6)
                WHERE `account_id` = @account_id;
                """);
            AddSessionParameters(update, accountId, characterId, sessionUid);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }

        await RequestKickAsync(connection, transaction, holder.RealmId, accountId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return false;
    }

    public async ValueTask LeaveAsync(uint accountId, uint sessionUid, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(accountId);
        ArgumentOutOfRangeException.ThrowIfZero(sessionUid);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = CreateCommand(connection, transaction: null, """
            DELETE FROM `account_sessions`
            WHERE `account_id` = @account_id
              AND `realm_id` = @realm_id
              AND `session_uid` = @session_uid;
            """);
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;
        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<AccountSessionKick>> TakeKicksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        List<AccountSessionKick> kicks = [];

        await using (MySqlCommand select = CreateCommand(connection, transaction, """
            SELECT `kick`.`account_id`, `session`.`character_id`, `session`.`session_uid`
            FROM `account_session_kicks` AS `kick`
            LEFT JOIN `account_sessions` AS `session`
                ON `session`.`account_id` = `kick`.`account_id`
               AND `session`.`realm_id` = `kick`.`realm_id`
            WHERE `kick`.`realm_id` = @realm_id
            FOR UPDATE OF `kick`;
            """))
        {
            select.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;

            await using MySqlDataReader reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                kicks.Add(new AccountSessionKick(
                    reader.GetUInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetUInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetUInt32(2)));
            }
        }

        // Deleted by key so that a request made after the read above is kept for the next poll.
        foreach (AccountSessionKick kick in kicks)
        {
            await using MySqlCommand delete = CreateCommand(connection, transaction,
                "DELETE FROM `account_session_kicks` WHERE `realm_id` = @realm_id AND `account_id` = @account_id;");
            delete.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;
            delete.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = kick.AccountId;
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

        return kicks;
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        await using MySqlCommand command = CreateCommand(connection, transaction, """
            DELETE FROM `account_session_kicks` WHERE `realm_id` = @realm_id;
            DELETE FROM `account_sessions` WHERE `realm_id` = @realm_id;
            """);
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask<Holder> ReadHolderAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = CreateCommand(connection, transaction, """
            SELECT `session`.`realm_id`, `session`.`session_uid`, COALESCE(`status`.`expires_at_utc` > UTC_TIMESTAMP(6), FALSE)
            FROM `account_sessions` AS `session`
            LEFT JOIN `realm_status` AS `status` ON `status`.`realm_id` = `session`.`realm_id`
            WHERE `session`.`account_id` = @account_id;
            """);
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Account {accountId} has no session row after it was locked.");
        }

        return new Holder(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetBoolean(2));
    }

    private static async ValueTask RequestKickAsync(MySqlConnection connection, MySqlTransaction transaction, uint realmId, uint accountId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = CreateCommand(connection, transaction, AccountSessionKickSql.Request);
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = realmId;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddSessionParameters(MySqlCommand command, uint accountId, uint characterId, uint sessionUid)
    {
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@realm_id", MySqlDbType.UInt32).Value = _realmId.Value;
        command.Parameters.Add("@character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;
    }

    private static MySqlCommand CreateCommand(MySqlConnection connection, MySqlTransaction? transaction, string commandText)
    {
        MySqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private readonly record struct Holder(uint RealmId, uint SessionUid, bool IsRealmReporting);
}
