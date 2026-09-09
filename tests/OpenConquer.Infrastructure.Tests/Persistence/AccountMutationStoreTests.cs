using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Audit;
using OpenConquer.Infrastructure.Persistence.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Mutations;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountMutationStoreTests : IClassFixture<AccountDatabaseFixture>, IAsyncLifetime
{
    private const ushort ActiveVerificationKeyId = 7;
    private const string PasswordHash = "$test$mutation$original";
    private const string ReplacementPasswordHash = "$test$mutation$replacement";

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly AccountDatabaseFixture _database;
    private readonly MySqlDataSource _dataSource;
    private readonly AccountMutationStore _store;
    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing;
    private readonly GameLoginTicketGrantStore _grantStore;

    private static int s_nextSessionUid = 10_000;
    private static int s_nextAuthenticationKey = 100_000;

    public AccountMutationStoreTests(AccountDatabaseFixture database)
    {
        _database = database;

        MySqlConnectionStringBuilder connection = new(database.RuntimeConnectionString)
        {
            AutoEnlist = false,
            DateTimeKind = MySqlDateTimeKind.Utc,
            GuidFormat = MySqlGuidFormat.Binary16,
            UseAffectedRows = false,
        };

        _dataSource = new MySqlDataSource(connection.ConnectionString);
        _store = new AccountMutationStore(_dataSource);
        _authenticationKeyRing = CreateAuthenticationKeyRing();
        _grantStore = new GameLoginTicketGrantStore(_dataSource, _authenticationKeyRing);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _authenticationKeyRing.Dispose();
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public void Constructor_RejectsNullDataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountMutationStore(null!));
    }

    [Fact]
    public async Task ChangeAccessStatus_ActiveToSuspendedRevokesTicketsAndAuditsMutation()
    {
        AccountRecord account = await InsertAccountAsync();
        AccountRecord other = await InsertAccountAsync();

        await InsertTicketAsync(account, 1001);
        await InsertTicketAsync(account, 1002);
        await InsertTicketAsync(other, 1003);

        Guid correlationId = Guid.NewGuid();
        AccountMutationContext context = AccountMutationContext.ForSystem(correlationId, "suspension");
        DateTime databaseUtcBefore = await ReadDatabaseUtcNowAsync();

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(account.AccountId, AccountAccessStatus.Suspended, account.StateRevision, context, CancellationToken);

        DateTime databaseUtcAfter = await ReadDatabaseUtcNowAsync();
        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAccessStatus.Suspended, persisted.AccessStatus);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Equal(AccountActorKind.System, persisted.StateChangedByActorKind);
        Assert.Null(persisted.StateChangedByAccountId);
        Assert.InRange(persisted.StateChangedAtUtc, databaseUtcBefore, databaseUtcAfter);

        Assert.Equal(AccountAuditEventKind.AccountSuspended, audit.EventKind);
        Assert.Equal(persisted.StateChangedAtUtc, audit.OccurredAtUtc);
        Assert.Equal(AccountActorKind.System, audit.ActorKind);
        Assert.Null(audit.ActorAccountId);
        Assert.Equal(correlationId, audit.CorrelationId);
        Assert.Equal("suspension", audit.ReasonCode);
        Assert.Equal(AccountAccessStatus.Active, audit.PreviousAccessStatus);
        Assert.Equal(AccountAccessStatus.Suspended, audit.NewAccessStatus);
        Assert.Null(audit.PreviousAuthorityRole);
        Assert.Null(audit.NewAuthorityRole);

        Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(other.AccountId));
    }

    [Theory]
    [InlineData(AccountAccessStatus.Active)]
    [InlineData(AccountAccessStatus.Suspended)]
    public async Task ChangeAccessStatus_ToBannedRevokesTickets(AccountAccessStatus initialStatus)
    {
        AccountRecord account = await InsertAccountAsync(initialStatus);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Banned,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "ban"),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAccessStatus.Banned, persisted.AccessStatus);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Equal(AccountAuditEventKind.AccountBanned, audit.EventKind);
        Assert.Equal(initialStatus, audit.PreviousAccessStatus);
        Assert.Equal(AccountAccessStatus.Banned, audit.NewAccessStatus);
        Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_BannedToActiveDoesNotRevokeTickets()
    {
        AccountRecord account = await InsertAccountAsync(AccountAccessStatus.Banned);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Active,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "unban"),
            CancellationToken);

        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAuditEventKind.AccountUnbanned, audit.EventKind);
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_BannedToSuspendedDoesNotRevokeTickets()
    {
        AccountRecord account = await InsertAccountAsync(AccountAccessStatus.Banned);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Suspended,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "unban-to-suspended"),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAccessStatus.Suspended, persisted.AccessStatus);
        Assert.Equal(AccountAuditEventKind.AccountUnbanned, audit.EventKind);
        Assert.Equal(AccountAccessStatus.Banned, audit.PreviousAccessStatus);
        Assert.Equal(AccountAccessStatus.Suspended, audit.NewAccessStatus);
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_NoChangeHasNoSideEffects()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Active,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.NoChange, status);
        Assert.Equal(1ul, persisted.StateRevision);
        Assert.Equal(s_createdAtUtc, persisted.StateChangedAtUtc);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_StaleRevisionRollsBackWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Banned,
            checked(account.StateRevision + 1),
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.StateConflict, status);
        Assert.Equal(AccountAccessStatus.Active, persisted.AccessStatus);
        Assert.Equal(1ul, persisted.StateRevision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAuthorityRole_UpdatesStateWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAuthorityRoleAsync(
            account.AccountId,
            AccountAuthorityRole.Gm,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "promotion"),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAuthorityRole.Gm, persisted.AuthorityRole);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Equal(AccountAuditEventKind.AuthorityRoleChanged, audit.EventKind);
        Assert.Equal(AccountAuthorityRole.Player, audit.PreviousAuthorityRole);
        Assert.Equal(AccountAuthorityRole.Gm, audit.NewAuthorityRole);
        Assert.Null(audit.PreviousAccessStatus);
        Assert.Null(audit.NewAccessStatus);
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task DeleteAccount_RevokesTicketsAndPersistsConsistentDeletionAudit()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationContext context = AccountMutationContext.ForMigration(Guid.NewGuid(), "retirement");

        AccountMutationStatus status = await _store.DeleteAccountAsync(account.AccountId, account.StateRevision, context, CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.NotNull(persisted.DeletedAtUtc);
        Assert.Equal(persisted.DeletedAtUtc, persisted.StateChangedAtUtc);
        Assert.Equal(AccountActorKind.Migration, persisted.DeletedByActorKind);
        Assert.Equal(AccountActorKind.Migration, persisted.StateChangedByActorKind);
        Assert.Null(persisted.DeletedByAccountId);
        Assert.Null(persisted.StateChangedByAccountId);
        Assert.Equal(2ul, persisted.StateRevision);

        Assert.Equal(AccountAuditEventKind.AccountDeleted, audit.EventKind);
        Assert.Equal(persisted.DeletedAtUtc, audit.OccurredAtUtc);
        Assert.Equal(AccountActorKind.Migration, audit.ActorKind);
        Assert.Equal("retirement", audit.ReasonCode);
        Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task RestoreAccount_ClearsDeletionStateWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await MarkDeletedAsync(account.AccountId);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.RestoreAccountAsync(
            account.AccountId,
            expectedStateRevision: 2,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "restore"),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Null(persisted.DeletedAtUtc);
        Assert.Null(persisted.DeletedByActorKind);
        Assert.Null(persisted.DeletedByAccountId);
        Assert.Equal(3ul, persisted.StateRevision);
        Assert.Equal(AccountAuditEventKind.AccountRestored, audit.EventKind);
        Assert.Equal(persisted.StateChangedAtUtc, audit.OccurredAtUtc);
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ResetPassword_ChangesOnlyCredentialRevisionAndRevokesTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());
        await InsertTicketAsync(account, NextSessionUid());

        DateTime databaseUtcBefore = await ReadDatabaseUtcNowAsync();

        AccountMutationStatus status = await _store.ResetPasswordAsync(
            account.AccountId,
            ReplacementPasswordHash,
            account.StateRevision,
            account.PasswordCredential.Revision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "credential-reset"),
            CancellationToken);

        DateTime databaseUtcAfter = await ReadDatabaseUtcNowAsync();
        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(1ul, persisted.StateRevision);
        Assert.Equal(s_createdAtUtc, persisted.StateChangedAtUtc);

        Assert.Equal(ReplacementPasswordHash, persisted.PasswordCredential.PasswordHash);
        Assert.Equal(2ul, persisted.PasswordCredential.Revision);
        Assert.InRange(persisted.PasswordCredential.PasswordChangedAtUtc, databaseUtcBefore, databaseUtcAfter);

        Assert.Equal(AccountAuditEventKind.PasswordChanged, audit.EventKind);
        Assert.Equal(persisted.PasswordCredential.PasswordChangedAtUtc, audit.OccurredAtUtc);
        Assert.Equal("credential-reset", audit.ReasonCode);
        Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ResetPassword_StaleCredentialRevisionRollsBackWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ResetPasswordAsync(
            account.AccountId,
            ReplacementPasswordHash,
            account.StateRevision,
            checked(account.PasswordCredential.Revision + 1),
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.StateConflict, status);
        Assert.Equal(PasswordHash, persisted.PasswordCredential.PasswordHash);
        Assert.Equal(1ul, persisted.PasswordCredential.Revision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenGrantWinsRace_RevokesGrantedTicket()
    {
        AccountRecord account = await InsertAccountAsync();
        GameLoginTicketGrantRequest request = CreateGrantRequest(account);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, CancellationToken);

        Task<GameLoginTicketGrantResult>? grantTask = null;
        Task<AccountMutationStatus>? mutationTask = null;
        bool gateReleased = false;

        try
        {
            await LockPasswordCredentialAsync(gateConnection, gateTransaction, account.AccountId);

            grantTask = _grantStore.TryGrantAsync(request, TimeSpan.FromMinutes(5), account.StateRevision, account.PasswordCredential.Revision, CancellationToken).AsTask();

            await WaitForInnoDbLockWaitAsync("account_password_credentials");
            Assert.False(grantTask.IsCompleted);

            mutationTask = _store.ChangeAccessStatusAsync(
                account.AccountId,
                AccountAccessStatus.Banned,
                account.StateRevision,
                AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-ban"),
                CancellationToken).AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(mutationTask.IsCompleted);

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            GameLoginTicketGrantResult grant = await grantTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
            AccountMutationStatus mutation = await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

            Assert.Equal(GameLoginTicketGrantStatus.Granted, grant.Status);
            Assert.Equal(AccountMutationStatus.Applied, mutation);
            Assert.Equal(AccountAccessStatus.Banned, (await ReadAccountAsync(account.AccountId)).AccessStatus);
            Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
        }
        finally
        {
            if (!gateReleased)
            {
                try
                {
                    await gateTransaction.RollbackAsync(CancellationToken.None);
                }
                catch (InvalidOperationException) { }
            }

            if (grantTask is not null)
            {
                try
                {
                    await grantTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }

            if (mutationTask is not null)
            {
                try
                {
                    await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenMutationWinsRace_RejectsStaleGrant()
    {
        AccountRecord account = await InsertAccountAsync();
        uint blockingSessionUid = NextSessionUid();

        await InsertTicketAsync(account, blockingSessionUid);

        GameLoginTicketGrantRequest request = CreateGrantRequest(account);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, CancellationToken);

        Task<AccountMutationStatus>? mutationTask = null;
        Task<GameLoginTicketGrantResult>? grantTask = null;
        bool gateReleased = false;

        try
        {
            await LockTicketAsync(gateConnection, gateTransaction, blockingSessionUid);

            mutationTask = _store.ChangeAccessStatusAsync(
                account.AccountId,
                AccountAccessStatus.Suspended,
                account.StateRevision,
                AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-suspension"),
                CancellationToken).AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");
            Assert.False(mutationTask.IsCompleted);

            grantTask = _grantStore.TryGrantAsync(request, TimeSpan.FromMinutes(5), account.StateRevision, account.PasswordCredential.Revision, CancellationToken).AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(grantTask.IsCompleted);

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            AccountMutationStatus mutation = await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
            GameLoginTicketGrantResult grant = await grantTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);

            Assert.Equal(AccountMutationStatus.Applied, mutation);
            Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, grant.Status);
            Assert.Null(grant.Ticket);
            Assert.Equal(AccountAccessStatus.Suspended, (await ReadAccountAsync(account.AccountId)).AccessStatus);
            Assert.Equal(0, await ReadTicketCountAsync(account.AccountId));
        }
        finally
        {
            if (!gateReleased)
            {
                try
                {
                    await gateTransaction.RollbackAsync(CancellationToken.None);
                }
                catch (InvalidOperationException) { }
            }

            if (mutationTask is not null)
            {
                try
                {
                    await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }

            if (grantTask is not null)
            {
                try
                {
                    await grantTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_SuspendedToActiveDoesNotRevokeTickets()
    {
        AccountRecord account = await InsertAccountAsync(AccountAccessStatus.Suspended);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Active,
            account.StateRevision,
            AccountMutationContext.ForSystem(Guid.NewGuid(), "reactivation"),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);
        AccountAuditEventRecord audit = Assert.Single(await ReadAuditEventsAsync(account.AccountId));

        Assert.Equal(AccountMutationStatus.Applied, status);
        Assert.Equal(AccountAccessStatus.Active, persisted.AccessStatus);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Equal(AccountAuditEventKind.AccountReactivated, audit.EventKind);
        Assert.Equal(AccountAccessStatus.Suspended, audit.PreviousAccessStatus);
        Assert.Equal(AccountAccessStatus.Active, audit.NewAccessStatus);
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAccessStatus_DeletedAccountReturnsInvalidStateWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await MarkDeletedAsync(account.AccountId);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ChangeAccessStatusAsync(
            account.AccountId,
            AccountAccessStatus.Banned,
            expectedStateRevision: 2,
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.InvalidState, status);
        Assert.NotNull(persisted.DeletedAtUtc);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ChangeAuthorityRole_DeletedAccountReturnsInvalidStateWithoutSideEffects()
    {
        AccountRecord account = await InsertAccountAsync();
        await MarkDeletedAsync(account.AccountId);

        AccountMutationStatus status = await _store.ChangeAuthorityRoleAsync(
            account.AccountId,
            AccountAuthorityRole.Gm,
            expectedStateRevision: 2,
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.InvalidState, status);
        Assert.Equal(AccountAuthorityRole.Player, persisted.AuthorityRole);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
    }

    [Fact]
    public async Task ResetPassword_StaleAccountRevisionRollsBackWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ResetPasswordAsync(
            account.AccountId,
            ReplacementPasswordHash,
            checked(account.StateRevision + 1),
            account.PasswordCredential.Revision,
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.StateConflict, status);
        Assert.Equal(PasswordHash, persisted.PasswordCredential.PasswordHash);
        Assert.Equal(1ul, persisted.PasswordCredential.Revision);
        Assert.Equal(1ul, persisted.StateRevision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    [Fact]
    public async Task ResetPassword_DeletedAccountReturnsInvalidStateWithoutRevokingTickets()
    {
        AccountRecord account = await InsertAccountAsync();
        await MarkDeletedAsync(account.AccountId);
        await InsertTicketAsync(account, NextSessionUid());

        AccountMutationStatus status = await _store.ResetPasswordAsync(
            account.AccountId,
            ReplacementPasswordHash,
            expectedStateRevision: 2,
            account.PasswordCredential.Revision,
            AccountMutationContext.ForSystem(Guid.NewGuid()),
            CancellationToken);

        AccountRecord persisted = await ReadAccountAsync(account.AccountId);

        Assert.Equal(AccountMutationStatus.InvalidState, status);
        Assert.Equal(PasswordHash, persisted.PasswordCredential.PasswordHash);
        Assert.Equal(1ul, persisted.PasswordCredential.Revision);
        Assert.Equal(2ul, persisted.StateRevision);
        Assert.Empty(await ReadAuditEventsAsync(account.AccountId));
        Assert.Equal(1, await ReadTicketCountAsync(account.AccountId));
    }

    private async Task LockPasswordCredentialAsync(MySqlConnection connection, MySqlTransaction transaction, uint accountId)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "SELECT `account_id` FROM `account_password_credentials` WHERE `account_id` = @account_id FOR UPDATE;";
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        Assert.NotNull(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task LockTicketAsync(MySqlConnection connection, MySqlTransaction transaction, uint sessionUid)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "SELECT `session_uid` FROM `game_login_tickets` WHERE `session_uid` = @session_uid FOR UPDATE;";
        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        Assert.NotNull(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task WaitForInnoDbLockWaitAsync(string tableName)
    {
        const int MaximumPollingAttempts = 500;

        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        for (int attempt = 0; attempt < MaximumPollingAttempts; attempt++)
        {
            await using MySqlCommand command = connection.CreateCommand();

            command.CommandText = """
                SELECT COUNT(*)
                FROM `performance_schema`.`data_lock_waits` AS `waits`
                INNER JOIN `performance_schema`.`data_locks` AS `requesting_lock`
                    ON `requesting_lock`.`ENGINE_LOCK_ID` = `waits`.`REQUESTING_ENGINE_LOCK_ID`
                WHERE `requesting_lock`.`OBJECT_SCHEMA` = @schema_name
                  AND `requesting_lock`.`OBJECT_NAME` = @table_name
                  AND `requesting_lock`.`LOCK_STATUS` = 'WAITING';
                """;

            command.Parameters.Add("@schema_name", MySqlDbType.VarChar).Value = AccountDatabaseFixture.DatabaseName;
            command.Parameters.Add("@table_name", MySqlDbType.VarChar).Value = tableName;

            long waiting = Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken), System.Globalization.CultureInfo.InvariantCulture);

            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        throw new TimeoutException($"MySQL did not report the expected InnoDB row-lock wait for table '{tableName}'.");
    }

    private async Task<AccountRecord> InsertAccountAsync(AccountAccessStatus accessStatus = AccountAccessStatus.Active)
    {
        AccountRecord account = new()
        {
            Username = "Mut" + Guid.NewGuid().ToString("N")[..29],
            AccessStatus = accessStatus,
            AuthorityRole = AccountAuthorityRole.Player,
            CreationOperationId = Guid.NewGuid(),
            StateRevision = 1,
            CreatedAtUtc = s_createdAtUtc,
            CreatedByActorKind = AccountActorKind.System,
            StateChangedAtUtc = s_createdAtUtc,
            StateChangedByActorKind = AccountActorKind.System,
        };

        account.PasswordCredential = new AccountPasswordCredentialRecord
        {
            PasswordHash = PasswordHash,
            PasswordChangedAtUtc = s_createdAtUtc,
            Revision = 1,
            Account = account,
        };

        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);

        db.Accounts.Add(account);
        await db.SaveChangesAsync(CancellationToken);

        return account;
    }

    private async Task MarkDeletedAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);
        AccountRecord account = await db.Accounts.SingleAsync(candidate => candidate.AccountId == accountId, CancellationToken);

        DateTime deletedAtUtc = s_createdAtUtc.AddMinutes(1);

        account.StateRevision = 2;
        account.StateChangedAtUtc = deletedAtUtc;
        account.StateChangedByActorKind = AccountActorKind.System;
        account.DeletedAtUtc = deletedAtUtc;
        account.DeletedByActorKind = AccountActorKind.System;

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task InsertTicketAsync(AccountRecord account, uint sessionUid)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO `game_login_tickets`
                (`session_uid`, `account_id`, `username`, `issued_at_utc`, `expires_at_utc`,
                 `authentication_key_verifier`, `authentication_key_verifier_key_id`)
            VALUES
                (@session_uid, @account_id, @username, @issued_at_utc, @expires_at_utc,
                 @authentication_key_verifier, @authentication_key_verifier_key_id);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = account.AccountId;
        command.Parameters.Add("@username", MySqlDbType.VarChar, AccountCredentialPolicy.MaximumUsernameLength).Value = account.Username;
        command.Parameters.Add("@issued_at_utc", MySqlDbType.DateTime).Value = s_createdAtUtc.AddMinutes(1);
        command.Parameters.Add("@expires_at_utc", MySqlDbType.DateTime).Value = s_createdAtUtc.AddMinutes(6);
        command.Parameters.Add("@authentication_key_verifier", MySqlDbType.Binary, 32).Value = new byte[32];
        command.Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16).Value = (ushort)1;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private async Task<AccountRecord> ReadAccountAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);

        return await db.Accounts.AsNoTracking()
            .Include(account => account.PasswordCredential)
            .SingleAsync(account => account.AccountId == accountId, CancellationToken);
    }

    private async Task<List<AccountAuditEventRecord>> ReadAuditEventsAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);

        return await db.AccountAuditEvents.AsNoTracking()
            .Where(audit => audit.AccountId == accountId)
            .OrderBy(audit => audit.AccountAuditEventId)
            .ToListAsync(CancellationToken);
    }

    private async Task<int> ReadTicketCountAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);

        return await db.GameLoginTickets.CountAsync(ticket => ticket.AccountId == accountId, CancellationToken);
    }

    private async Task<DateTime> ReadDatabaseUtcNowAsync()
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = "SELECT UTC_TIMESTAMP(6);";

        DateTime result = Assert.IsType<DateTime>(await command.ExecuteScalarAsync(CancellationToken));

        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }

    private static GameLoginTicketGrantRequest CreateGrantRequest(AccountRecord account)
    {
        return new GameLoginTicketGrantRequest(account.AccountId, account.Username, NextSessionUid(), NextAuthenticationKey());
    }

    private static GameLoginTicketAuthenticationKeyRing CreateAuthenticationKeyRing()
    {
        byte[] verificationKey = new byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];
        RandomNumberGenerator.Fill(verificationKey);

        try
        {
            return new GameLoginTicketAuthenticationKeyRing(
                ActiveVerificationKeyId,
                [new KeyValuePair<ushort, byte[]>(ActiveVerificationKeyId, verificationKey)]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    private static uint NextSessionUid()
    {
        uint value = unchecked((uint)Interlocked.Increment(ref s_nextSessionUid));

        return value == 0 ? unchecked((uint)Interlocked.Increment(ref s_nextSessionUid)) : value;
    }

    private static uint NextAuthenticationKey()
    {
        uint value = unchecked((uint)Interlocked.Increment(ref s_nextAuthenticationKey));

        return value == 0 ? unchecked((uint)Interlocked.Increment(ref s_nextAuthenticationKey)) : value;
    }
}
