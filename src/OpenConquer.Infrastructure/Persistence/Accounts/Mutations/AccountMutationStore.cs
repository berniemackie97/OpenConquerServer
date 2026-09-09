using System.Data;
using MySqlConnector;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Mutations;

internal sealed class AccountMutationStore(MySqlDataSource dataSource) : IAccountMutationStore
{
    private readonly MySqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async ValueTask<AccountMutationStatus> ChangeAccessStatusAsync(uint accountId, AccountAccessStatus newAccessStatus, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);

        if (!Enum.IsDefined(newAccessStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(newAccessStatus), "The requested account access status is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedAccountState? persisted = await LockAccountAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persisted is null)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.AccountNotFound).ConfigureAwait(false);
        }

        PersistedAccountState account = persisted.Value;

        if (account.StateRevision != expectedStateRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        if (account.IsDeleted)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.InvalidState).ConfigureAwait(false);
        }

        if (account.AccessStatus == newAccessStatus)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.NoChange).ConfigureAwait(false);
        }

        EnsureRevisionCanAdvance(account.StateRevision, "account state");

        AccountAuditEventKind eventKind = GetAccessStatusEventKind(account.AccessStatus, newAccessStatus);
        bool revokeTickets = eventKind is AccountAuditEventKind.AccountSuspended or AccountAuditEventKind.AccountBanned;

        int lockedTicketCount = revokeTickets
            ? await LockGameLoginTicketsAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false)
            : 0;

        DateTime occurredAtUtc = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        if (revokeTickets)
        {
            await DeleteLockedGameLoginTicketsAsync(connection, transaction, accountId, lockedTicketCount, cancellationToken).ConfigureAwait(false);
        }

        await UpdateAccessStatusAsync(connection, transaction, accountId, account.StateRevision, newAccessStatus, occurredAtUtc, context, cancellationToken).ConfigureAwait(false);
        await InsertAuditEventAsync(connection, transaction, accountId, eventKind, occurredAtUtc, context, account.AccessStatus, newAccessStatus, previousAuthorityRole: null, newAuthorityRole: null, cancellationToken).ConfigureAwait(false);

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);

        return AccountMutationStatus.Applied;
    }

    public async ValueTask<AccountMutationStatus> ChangeAuthorityRoleAsync(uint accountId, AccountAuthorityRole newAuthorityRole, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);

        if (!Enum.IsDefined(newAuthorityRole))
        {
            throw new ArgumentOutOfRangeException(nameof(newAuthorityRole), "The requested account authority role is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedAccountState? persisted = await LockAccountAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persisted is null)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.AccountNotFound).ConfigureAwait(false);
        }

        PersistedAccountState account = persisted.Value;

        if (account.StateRevision != expectedStateRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        if (account.IsDeleted)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.InvalidState).ConfigureAwait(false);
        }

        if (account.AuthorityRole == newAuthorityRole)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.NoChange).ConfigureAwait(false);
        }

        EnsureRevisionCanAdvance(account.StateRevision, "account state");

        DateTime occurredAtUtc = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await UpdateAuthorityRoleAsync(connection, transaction, accountId, account.StateRevision, newAuthorityRole, occurredAtUtc, context, cancellationToken).ConfigureAwait(false);
        await InsertAuditEventAsync(connection, transaction, accountId, AccountAuditEventKind.AuthorityRoleChanged, occurredAtUtc, context, previousAccessStatus: null, newAccessStatus: null, account.AuthorityRole, newAuthorityRole, cancellationToken).ConfigureAwait(false);

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);

        return AccountMutationStatus.Applied;
    }

    public async ValueTask<AccountMutationStatus> DeleteAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedAccountState? persisted = await LockAccountAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persisted is null)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.AccountNotFound).ConfigureAwait(false);
        }

        PersistedAccountState account = persisted.Value;

        if (account.StateRevision != expectedStateRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        if (account.IsDeleted)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.NoChange).ConfigureAwait(false);
        }

        EnsureRevisionCanAdvance(account.StateRevision, "account state");

        int lockedTicketCount = await LockGameLoginTicketsAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);
        DateTime occurredAtUtc = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await DeleteLockedGameLoginTicketsAsync(connection, transaction, accountId, lockedTicketCount, cancellationToken).ConfigureAwait(false);
        await SoftDeleteAccountAsync(connection, transaction, accountId, account.StateRevision, occurredAtUtc, context, cancellationToken).ConfigureAwait(false);
        await InsertAuditEventAsync(connection, transaction, accountId, AccountAuditEventKind.AccountDeleted, occurredAtUtc, context, previousAccessStatus: null, newAccessStatus: null, previousAuthorityRole: null, newAuthorityRole: null, cancellationToken).ConfigureAwait(false);

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);

        return AccountMutationStatus.Applied;
    }

    public async ValueTask<AccountMutationStatus> RestoreAccountAsync(uint accountId, ulong expectedStateRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);
        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedAccountState? persisted = await LockAccountAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persisted is null)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.AccountNotFound).ConfigureAwait(false);
        }

        PersistedAccountState account = persisted.Value;

        if (account.StateRevision != expectedStateRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        if (!account.IsDeleted)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.NoChange).ConfigureAwait(false);
        }

        EnsureRevisionCanAdvance(account.StateRevision, "account state");

        DateTime occurredAtUtc = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await RestoreDeletedAccountAsync(connection, transaction, accountId, account.StateRevision, occurredAtUtc, context, cancellationToken).ConfigureAwait(false);
        await InsertAuditEventAsync(connection, transaction, accountId, AccountAuditEventKind.AccountRestored, occurredAtUtc, context, previousAccessStatus: null, newAccessStatus: null, previousAuthorityRole: null, newAuthorityRole: null, cancellationToken).ConfigureAwait(false);

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);

        return AccountMutationStatus.Applied;
    }

    public async ValueTask<AccountMutationStatus> ResetPasswordAsync(uint accountId, string newPasswordHash, ulong expectedStateRevision, ulong expectedPasswordCredentialRevision, AccountMutationContext context, CancellationToken cancellationToken = default)
    {
        ValidateTarget(accountId, expectedStateRevision, context);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        if (context.ActorKind != AccountActorKind.System)
        {
            throw new ArgumentException("Password reset requires a system actor.", nameof(context));
        }

        if (expectedPasswordCredentialRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPasswordCredentialRevision), "A password reset requires a valid password-credential revision.");
        }

        if (newPasswordHash.Length > AccountPasswordCredentialConfiguration.MaximumPasswordHashLength)
        {
            throw new ArgumentException($"A password hash cannot exceed {AccountPasswordCredentialConfiguration.MaximumPasswordHashLength} characters.", nameof(newPasswordHash));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        PersistedAccountState? persisted = await LockAccountAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persisted is null)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.AccountNotFound).ConfigureAwait(false);
        }

        PersistedAccountState account = persisted.Value;

        if (account.StateRevision != expectedStateRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        if (account.IsDeleted)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.InvalidState).ConfigureAwait(false);
        }

        ulong? persistedCredentialRevision = await LockPasswordCredentialAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        if (persistedCredentialRevision is null)
        {
            throw new InvalidOperationException("The account does not have its required password credential.");
        }

        if (persistedCredentialRevision.Value != expectedPasswordCredentialRevision)
        {
            return await RollbackAsync(transaction, AccountMutationStatus.StateConflict).ConfigureAwait(false);
        }

        EnsureRevisionCanAdvance(persistedCredentialRevision.Value, "password credential");

        int lockedTicketCount = await LockGameLoginTicketsAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);
        DateTime occurredAtUtc = await ReadDatabaseUtcNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await DeleteLockedGameLoginTicketsAsync(connection, transaction, accountId, lockedTicketCount, cancellationToken).ConfigureAwait(false);
        await UpdatePasswordCredentialAsync(connection, transaction, accountId, persistedCredentialRevision.Value, newPasswordHash, occurredAtUtc, cancellationToken).ConfigureAwait(false);
        await InsertAuditEventAsync(connection, transaction, accountId, AccountAuditEventKind.PasswordChanged, occurredAtUtc, context, previousAccessStatus: null, newAccessStatus: null, previousAuthorityRole: null, newAuthorityRole: null, cancellationToken).ConfigureAwait(false);

        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);

        return AccountMutationStatus.Applied;
    }

    private static async ValueTask<PersistedAccountState?> LockAccountAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                `access_status`,
                `authority_role`,
                `deleted_at_utc`,
                `state_revision`
            FROM `accounts`
            WHERE `account_id` = @account_id
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        AccountAccessStatus accessStatus = (AccountAccessStatus)reader.GetByte(0);
        AccountAuthorityRole authorityRole = (AccountAuthorityRole)reader.GetByte(1);
        bool isDeleted = !reader.IsDBNull(2);
        ulong stateRevision = reader.GetUInt64(3);

        if (!Enum.IsDefined(accessStatus) || !Enum.IsDefined(authorityRole) || stateRevision == 0)
        {
            throw new InvalidOperationException("The persisted account contains invalid authorization state.");
        }

        return new PersistedAccountState(accessStatus, authorityRole, isDeleted, stateRevision);
    }

    private static async ValueTask<ulong?> LockPasswordCredentialAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT `revision`
            FROM `account_password_credentials`
            WHERE `account_id` = @account_id
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        ulong revision = Convert.ToUInt64(result);

        if (revision == 0)
        {
            throw new InvalidOperationException("The persisted password credential contains an invalid revision.");
        }

        return revision;
    }

    private static async ValueTask<int> LockGameLoginTicketsAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT `session_uid`
            FROM `game_login_tickets`
            WHERE `account_id` = @account_id
            ORDER BY `session_uid`
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        int count = 0;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            _ = reader.GetUInt32(0);
            count = checked(count + 1);
        }

        return count;
    }

    private static async ValueTask DeleteLockedGameLoginTicketsAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, int expectedCount, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM `game_login_tickets`
            WHERE `account_id` = @account_id;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != expectedCount)
        {
            throw new InvalidOperationException($"Game-login ticket revocation affected an unexpected number of rows. Expected {expectedCount}, but MySQL reported {affected}.");
        }
    }

    private static async ValueTask<DateTime> ReadDatabaseUtcNowAsync(MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (result is not DateTime databaseUtcNow)
        {
            throw new InvalidOperationException("MySQL did not return a valid UTC timestamp during account mutation.");
        }

        return DateTime.SpecifyKind(databaseUtcNow, DateTimeKind.Utc);
    }

    private static async ValueTask UpdateAccessStatusAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedStateRevision, AccountAccessStatus accessStatus, DateTime occurredAtUtc, AccountMutationContext context, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `accounts`
            SET `access_status` = @access_status,
                `state_revision` = `state_revision` + 1,
                `state_changed_at_utc` = @occurred_at_utc,
                `state_changed_by_actor_kind` = @actor_kind,
                `state_changed_by_account_id` = NULL
            WHERE `account_id` = @account_id
              AND `deleted_at_utc` IS NULL
              AND `state_revision` = @expected_state_revision;
            """;

        AddAccountStateMutationParameters(command, accountId, expectedStateRevision, occurredAtUtc, context);
        command.Parameters.Add("@access_status", MySqlDbType.UByte).Value = (byte)accessStatus;

        await RequireSingleAffectedRowAsync(command, "account access-status mutation", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask UpdateAuthorityRoleAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedStateRevision, AccountAuthorityRole authorityRole, DateTime occurredAtUtc, AccountMutationContext context, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `accounts`
            SET `authority_role` = @authority_role,
                `state_revision` = `state_revision` + 1,
                `state_changed_at_utc` = @occurred_at_utc,
                `state_changed_by_actor_kind` = @actor_kind,
                `state_changed_by_account_id` = NULL
            WHERE `account_id` = @account_id
              AND `deleted_at_utc` IS NULL
              AND `state_revision` = @expected_state_revision;
            """;

        AddAccountStateMutationParameters(command, accountId, expectedStateRevision, occurredAtUtc, context);
        command.Parameters.Add("@authority_role", MySqlDbType.UByte).Value = (byte)authorityRole;

        await RequireSingleAffectedRowAsync(command, "account authority-role mutation", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SoftDeleteAccountAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedStateRevision, DateTime occurredAtUtc, AccountMutationContext context, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `accounts`
            SET `deleted_at_utc` = @occurred_at_utc,
                `deleted_by_actor_kind` = @actor_kind,
                `deleted_by_account_id` = NULL,
                `state_revision` = `state_revision` + 1,
                `state_changed_at_utc` = @occurred_at_utc,
                `state_changed_by_actor_kind` = @actor_kind,
                `state_changed_by_account_id` = NULL
            WHERE `account_id` = @account_id
              AND `deleted_at_utc` IS NULL
              AND `state_revision` = @expected_state_revision;
            """;

        AddAccountStateMutationParameters(command, accountId, expectedStateRevision, occurredAtUtc, context);

        await RequireSingleAffectedRowAsync(command, "account deletion", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask RestoreDeletedAccountAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedStateRevision, DateTime occurredAtUtc, AccountMutationContext context, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `accounts`
            SET `deleted_at_utc` = NULL,
                `deleted_by_actor_kind` = NULL,
                `deleted_by_account_id` = NULL,
                `state_revision` = `state_revision` + 1,
                `state_changed_at_utc` = @occurred_at_utc,
                `state_changed_by_actor_kind` = @actor_kind,
                `state_changed_by_account_id` = NULL
            WHERE `account_id` = @account_id
              AND `deleted_at_utc` IS NOT NULL
              AND `state_revision` = @expected_state_revision;
            """;

        AddAccountStateMutationParameters(command, accountId, expectedStateRevision, occurredAtUtc, context);

        await RequireSingleAffectedRowAsync(command, "account restoration", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask UpdatePasswordCredentialAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, ulong expectedPasswordCredentialRevision, string newPasswordHash, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE `account_password_credentials`
            SET `password_hash` = @password_hash,
                `password_changed_at_utc` = @occurred_at_utc,
                `revision` = `revision` + 1
            WHERE `account_id` = @account_id
              AND `revision` = @expected_password_credential_revision;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@expected_password_credential_revision", MySqlDbType.UInt64).Value = expectedPasswordCredentialRevision;
        command.Parameters.Add("@password_hash", MySqlDbType.VarChar, AccountPasswordCredentialConfiguration.MaximumPasswordHashLength).Value = newPasswordHash;
        command.Parameters.Add("@occurred_at_utc", MySqlDbType.DateTime).Value = occurredAtUtc;

        await RequireSingleAffectedRowAsync(command, "account password reset", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask InsertAuditEventAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId, AccountAuditEventKind eventKind, DateTime occurredAtUtc, AccountMutationContext context, AccountAccessStatus? previousAccessStatus, AccountAccessStatus? newAccessStatus, AccountAuthorityRole? previousAuthorityRole, AccountAuthorityRole? newAuthorityRole, CancellationToken cancellationToken)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO `account_audit_events`
                (`account_id`,
                 `event_kind`,
                 `occurred_at_utc`,
                 `actor_kind`,
                 `actor_account_id`,
                 `correlation_id`,
                 `reason_code`,
                 `previous_access_status`,
                 `new_access_status`,
                 `previous_authority_role`,
                 `new_authority_role`)
            VALUES
                (@account_id,
                 @event_kind,
                 @occurred_at_utc,
                 @actor_kind,
                 NULL,
                 @correlation_id,
                 @reason_code,
                 @previous_access_status,
                 @new_access_status,
                 @previous_authority_role,
                 @new_authority_role);
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@event_kind", MySqlDbType.UByte).Value = (byte)eventKind;
        command.Parameters.Add("@occurred_at_utc", MySqlDbType.DateTime).Value = occurredAtUtc;
        command.Parameters.Add("@actor_kind", MySqlDbType.UByte).Value = (byte)context.ActorKind;
        command.Parameters.Add("@correlation_id", MySqlDbType.Guid).Value = context.CorrelationId;
        command.Parameters.Add("@reason_code", MySqlDbType.VarChar, AccountAuditPolicy.MaximumReasonCodeLength).Value = (object?)context.ReasonCode ?? DBNull.Value;
        command.Parameters.Add("@previous_access_status", MySqlDbType.UByte).Value = previousAccessStatus.HasValue ? (object)(byte)previousAccessStatus.Value : DBNull.Value;
        command.Parameters.Add("@new_access_status", MySqlDbType.UByte).Value = newAccessStatus.HasValue ? (object)(byte)newAccessStatus.Value : DBNull.Value;
        command.Parameters.Add("@previous_authority_role", MySqlDbType.UByte).Value = previousAuthorityRole.HasValue ? (object)(byte)previousAuthorityRole.Value : DBNull.Value;
        command.Parameters.Add("@new_authority_role", MySqlDbType.UByte).Value = newAuthorityRole.HasValue ? (object)(byte)newAuthorityRole.Value : DBNull.Value;

        await RequireSingleAffectedRowAsync(command, "account audit-event insertion", cancellationToken).ConfigureAwait(false);
    }

    private static void AddAccountStateMutationParameters(MySqlCommand command, uint accountId, ulong expectedStateRevision, DateTime occurredAtUtc, AccountMutationContext context)
    {
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@expected_state_revision", MySqlDbType.UInt64).Value = expectedStateRevision;
        command.Parameters.Add("@occurred_at_utc", MySqlDbType.DateTime).Value = occurredAtUtc;
        command.Parameters.Add("@actor_kind", MySqlDbType.UByte).Value = (byte)context.ActorKind;
    }

    private static async ValueTask RequireSingleAffectedRowAsync(MySqlCommand command, string operation, CancellationToken cancellationToken)
    {
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException($"{operation} affected an unexpected number of rows. Expected 1, but MySQL reported {affected}.");
        }
    }

    private static AccountAuditEventKind GetAccessStatusEventKind(AccountAccessStatus previousStatus, AccountAccessStatus newStatus)
    {
        return (previousStatus, newStatus) switch
        {
            (AccountAccessStatus.Active, AccountAccessStatus.Suspended) => AccountAuditEventKind.AccountSuspended,
            (AccountAccessStatus.Suspended, AccountAccessStatus.Active) => AccountAuditEventKind.AccountReactivated,
            (AccountAccessStatus.Active, AccountAccessStatus.Banned) => AccountAuditEventKind.AccountBanned,
            (AccountAccessStatus.Suspended, AccountAccessStatus.Banned) => AccountAuditEventKind.AccountBanned,
            (AccountAccessStatus.Banned, AccountAccessStatus.Active) => AccountAuditEventKind.AccountUnbanned,
            (AccountAccessStatus.Banned, AccountAccessStatus.Suspended) => AccountAuditEventKind.AccountUnbanned,
            _ => throw new InvalidOperationException($"Unsupported account access-status transition {previousStatus} -> {newStatus}."),
        };
    }

    private static void EnsureRevisionCanAdvance(ulong revision, string revisionName)
    {
        if (revision == ulong.MaxValue)
        {
            throw new InvalidOperationException($"The persisted {revisionName} revision has been exhausted.");
        }
    }

    private static async ValueTask<AccountMutationStatus> RollbackAsync(MySqlTransaction transaction, AccountMutationStatus status)
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return status;
    }

    private static async ValueTask CommitAsync(MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static void ValidateTarget(uint accountId, ulong expectedStateRevision, AccountMutationContext context)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An account mutation requires a persisted account.");
        }

        if (expectedStateRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStateRevision), "An account mutation requires a valid account-state revision.");
        }

        ArgumentNullException.ThrowIfNull(context);
    }

    private readonly record struct PersistedAccountState(AccountAccessStatus AccessStatus, AccountAuthorityRole AuthorityRole, bool IsDeleted, ulong StateRevision);
}
