using System.Data;
using MySqlConnector;
using OpenConquer.Application.Accounts.Registration;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Registration;

/// <summary>
/// Inserts a self-service account, its password credential and its creation audit event in one transaction,
/// using a dedicated registration database identity.
/// </summary>
public sealed class AccountRegistrationStore : IAccountRegistrationStore
{
    private const int CommandTimeoutSeconds = 15;
    private const int DuplicateKeyErrorNumber = 1062;
    private const string UsernameIndexName = "UX_accounts_username";

    private readonly string _connectionString;

    public AccountRegistrationStore(string connectionString)
    {
        _connectionString = AccountDbContextOptionsConfiguration.CreateConnectionString(connectionString);
    }

    public async ValueTask<uint?> TryCreateAsync(string username, string passwordHash, Guid creationOperationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (creationOperationId == Guid.Empty)
        {
            throw new ArgumentException("A registration requires a nonempty creation operation ID.", nameof(creationOperationId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // From here the statements run to completion: a write that has started is never abandoned half way.
        uint accountId;

        try
        {
            accountId = await InsertAccountAsync(connection, transaction, username, creationOperationId).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == DuplicateKeyErrorNumber &&
            exception.Message.Contains(UsernameIndexName, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return null;
        }

        await InsertCredentialAsync(connection, transaction, accountId, passwordHash).ConfigureAwait(false);
        await InsertCreationAuditAsync(connection, transaction, accountId, creationOperationId).ConfigureAwait(false);

        try
        {
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is MySqlException or IOException or TimeoutException)
        {
            // The commit may have reached the server even though its acknowledgment was lost.
            uint? committed = await FindByCreationOperationAsync(creationOperationId).ConfigureAwait(false);

            if (committed is uint recovered && recovered == accountId)
            {
                return recovered;
            }

            if (committed is null)
            {
                throw new InvalidOperationException("Account registration did not commit.", exception);
            }

            throw new InvalidOperationException("A registration operation ID resolved to a different account.", exception);
        }

        return accountId;
    }

    private static async ValueTask<uint> InsertAccountAsync(MySqlConnection connection, MySqlTransaction transaction,
        string username, Guid creationOperationId)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            INSERT INTO `accounts`
                (`username`,
                 `access_status`,
                 `authority_role`,
                 `creation_operation_id`,
                 `state_revision`,
                 `created_at_utc`,
                 `created_by_actor_kind`,
                 `created_by_account_id`,
                 `state_changed_at_utc`,
                 `state_changed_by_actor_kind`,
                 `state_changed_by_account_id`)
            VALUES
                (@username,
                 @access_status,
                 @authority_role,
                 @creation_operation_id,
                 1,
                 UTC_TIMESTAMP(6),
                 @created_by_actor_kind,
                 NULL,
                 UTC_TIMESTAMP(6),
                 @state_changed_by_actor_kind,
                 NULL);
            """;

        command.Parameters.Add("@username", MySqlDbType.VarChar, AccountCredentialPolicy.MaximumUsernameLength).Value = username;
        command.Parameters.Add("@access_status", MySqlDbType.UByte).Value = (byte)AccountAccessStatus.Active;
        command.Parameters.Add("@authority_role", MySqlDbType.UByte).Value = (byte)AccountAuthorityRole.Player;
        command.Parameters.Add("@creation_operation_id", MySqlDbType.Guid).Value = creationOperationId;
        command.Parameters.Add("@created_by_actor_kind", MySqlDbType.UByte).Value = (byte)AccountActorKind.SelfService;
        // The schema reserves self-service for creation; the initial access state is set by the system.
        command.Parameters.Add("@state_changed_by_actor_kind", MySqlDbType.UByte).Value = (byte)AccountActorKind.System;

        if (await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Account registration did not insert exactly one account row.");
        }

        long accountId = command.LastInsertedId;

        if (accountId is < 1 or > uint.MaxValue)
        {
            throw new InvalidOperationException("Account registration returned an invalid generated account ID.");
        }

        return (uint)accountId;
    }

    private static async ValueTask InsertCredentialAsync(MySqlConnection connection, MySqlTransaction transaction,
        uint accountId, string passwordHash)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            INSERT INTO `account_password_credentials`
                (`account_id`, `password_hash`, `password_changed_at_utc`, `revision`)
            SELECT `account_id`, @password_hash, `created_at_utc`, 1
            FROM `accounts`
            WHERE `account_id` = @account_id;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@password_hash", MySqlDbType.VarChar, 255).Value = passwordHash;

        if (await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Account registration did not insert exactly one password credential.");
        }
    }

    private static async ValueTask InsertCreationAuditAsync(MySqlConnection connection, MySqlTransaction transaction,
        uint accountId, Guid creationOperationId)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            INSERT INTO `account_audit_events`
                (`account_id`, `event_kind`, `occurred_at_utc`, `actor_kind`, `actor_account_id`, `correlation_id`,
                 `reason_code`, `previous_access_status`, `new_access_status`, `previous_authority_role`,
                 `new_authority_role`)
            SELECT `account_id`, @event_kind, `created_at_utc`, @actor_kind, NULL, @correlation_id,
                 NULL, NULL, `access_status`, NULL, `authority_role`
            FROM `accounts`
            WHERE `account_id` = @account_id;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@event_kind", MySqlDbType.UByte).Value = (byte)AccountAuditEventKind.AccountCreated;
        command.Parameters.Add("@actor_kind", MySqlDbType.UByte).Value = (byte)AccountActorKind.SelfService;
        command.Parameters.Add("@correlation_id", MySqlDbType.Guid).Value = creationOperationId;

        if (await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Account registration did not insert exactly one creation audit event.");
        }
    }

    private async ValueTask<uint?> FindByCreationOperationAsync(Guid creationOperationId)
    {
        await using MySqlConnection connection = new(_connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText =
            """
            SELECT `account_id`
            FROM `accounts`
            WHERE `creation_operation_id` = @creation_operation_id;
            """;

        command.Parameters.Add("@creation_operation_id", MySqlDbType.Guid).Value = creationOperationId;

        object? value = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);

        return value is null or DBNull ? null : Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
