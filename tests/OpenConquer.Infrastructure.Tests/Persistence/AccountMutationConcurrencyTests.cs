using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Mutations;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountMutationConcurrencyTests
    : IClassFixture<AccountDatabaseFixture>,
        IAsyncLifetime
{
    private const ushort ActiveVerificationKeyId = 7;
    private const string PasswordHash = "$test$mutation-race$original";
    private const string ReplacementPasswordHash = "$test$mutation-race$replacement";

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan s_ticketLifetime = TimeSpan.FromMinutes(5);
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly AccountDatabaseFixture _database;
    private readonly MySqlDataSource _dataSource;
    private readonly byte[] _verificationKey;
    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing;
    private readonly AccountMutationStore _mutationStore;
    private readonly GameLoginTicketGrantStore _grantStore;
    private readonly GameLoginTicketRedemptionStore _redemptionStore;

    private static int s_nextSessionUid = 1_000_000;
    private static int s_nextAuthenticationKey = 2_000_000;

    public AccountMutationConcurrencyTests(AccountDatabaseFixture database)
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
        _verificationKey = new byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];
        RandomNumberGenerator.Fill(_verificationKey);

        _authenticationKeyRing = new GameLoginTicketAuthenticationKeyRing(
            ActiveVerificationKeyId,
            [new KeyValuePair<ushort, byte[]>(ActiveVerificationKeyId, _verificationKey)]
        );

        _mutationStore = new AccountMutationStore(_dataSource);
        _grantStore = new GameLoginTicketGrantStore(_dataSource, _authenticationKeyRing);
        _redemptionStore = new GameLoginTicketRedemptionStore(_dataSource, _authenticationKeyRing);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _authenticationKeyRing.Dispose();
        CryptographicOperations.ZeroMemory(_verificationKey);
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task ResetPassword_WhenGrantWinsRace_RevokesGrantedTicket()
    {
        AccountRecord account = await InsertAccountAsync();
        GameLoginTicketGrantRequest request = CreateGrantRequest(account);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        Task<GameLoginTicketGrantResult>? grantTask = null;
        Task<AccountMutationStatus>? resetTask = null;
        bool gateReleased = false;

        try
        {
            await LockPasswordCredentialAsync(gateConnection, gateTransaction, account.AccountId);

            grantTask = _grantStore
                .TryGrantAsync(
                    request,
                    s_ticketLifetime,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("account_password_credentials");
            Assert.False(grantTask.IsCompleted);

            resetTask = _mutationStore
                .ResetPasswordAsync(
                    account.AccountId,
                    ReplacementPasswordHash,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-reset"),
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(resetTask.IsCompleted);

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            GameLoginTicketGrantResult grant = await grantTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );
            AccountMutationStatus reset = await resetTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            AccountRecord persisted = await ReadAccountAsync(account.AccountId);

            Assert.Equal(GameLoginTicketGrantStatus.Granted, grant.Status);
            Assert.Equal(AccountMutationStatus.Applied, reset);
            Assert.Equal(ReplacementPasswordHash, persisted.PasswordCredential.PasswordHash);
            Assert.Equal(2ul, persisted.PasswordCredential.Revision);
            Assert.False(await TicketExistsAsync(request.SessionUid));
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

            await ObserveTaskAsync(grantTask);
            await ObserveTaskAsync(resetTask);
        }
    }

    [Fact]
    public async Task ResetPassword_WhenResetWinsRace_RejectsStaleGrant()
    {
        AccountRecord account = await InsertAccountAsync();
        uint blockingSessionUid = NextSessionUid();
        uint blockingAuthenticationKey = NextAuthenticationKey();

        await InsertTicketAsync(account, blockingSessionUid, blockingAuthenticationKey);

        GameLoginTicketGrantRequest request = CreateGrantRequest(account);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        Task<AccountMutationStatus>? resetTask = null;
        Task<GameLoginTicketGrantResult>? grantTask = null;
        bool gateReleased = false;

        try
        {
            await LockTicketAsync(gateConnection, gateTransaction, blockingSessionUid);

            resetTask = _mutationStore
                .ResetPasswordAsync(
                    account.AccountId,
                    ReplacementPasswordHash,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-reset"),
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");
            Assert.False(resetTask.IsCompleted);

            grantTask = _grantStore
                .TryGrantAsync(
                    request,
                    s_ticketLifetime,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(grantTask.IsCompleted);

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            AccountMutationStatus reset = await resetTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );
            GameLoginTicketGrantResult grant = await grantTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            AccountRecord persisted = await ReadAccountAsync(account.AccountId);

            Assert.Equal(AccountMutationStatus.Applied, reset);
            Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, grant.Status);
            Assert.Null(grant.Ticket);
            Assert.Equal(ReplacementPasswordHash, persisted.PasswordCredential.PasswordHash);
            Assert.Equal(2ul, persisted.PasswordCredential.Revision);
            Assert.False(await TicketExistsAsync(blockingSessionUid));
            Assert.False(await TicketExistsAsync(request.SessionUid));
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

            await ObserveTaskAsync(resetTask);
            await ObserveTaskAsync(grantTask);
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenRedemptionWinsRace_RedeemsBeforeMutationCommits()
    {
        AccountRecord account = await InsertAccountAsync();
        uint sessionUid = NextSessionUid();
        uint authenticationKey = NextAuthenticationKey();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        Task<AccountMutationStatus>? mutationTask = null;
        bool gateReleased = false;

        try
        {
            await LockAccountAsync(gateConnection, gateTransaction, account.AccountId);

            mutationTask = _mutationStore
                .ChangeAccessStatusAsync(
                    account.AccountId,
                    AccountAccessStatus.Suspended,
                    account.StateRevision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-suspension"),
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(mutationTask.IsCompleted);

            GameLoginTicketIdentity? identity = await _redemptionStore.TryRedeemAsync(
                sessionUid,
                authenticationKey,
                CancellationToken
            );
            GameLoginTicketIdentity redeemed = Assert.IsType<GameLoginTicketIdentity>(identity);

            Assert.Equal(account.AccountId, redeemed.AccountId);
            Assert.Equal(account.Username, redeemed.Username);
            Assert.Equal(sessionUid, redeemed.SessionUid);
            Assert.False(await TicketExistsAsync(sessionUid));

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            AccountMutationStatus mutation = await mutationTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            Assert.Equal(AccountMutationStatus.Applied, mutation);
            Assert.Equal(
                AccountAccessStatus.Suspended,
                (await ReadAccountAsync(account.AccountId)).AccessStatus
            );
            Assert.False(await TicketExistsAsync(sessionUid));
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

            await ObserveTaskAsync(mutationTask);
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenRevocationWinsRace_RedemptionReturnsNull()
    {
        AccountRecord account = await InsertAccountAsync();
        uint sessionUid = NextSessionUid();
        uint authenticationKey = NextAuthenticationKey();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        Task<AccountMutationStatus>? mutationTask = null;
        Task<GameLoginTicketIdentity?>? redemptionTask = null;
        bool gateReleased = false;

        try
        {
            await LockTicketAsync(gateConnection, gateTransaction, sessionUid);

            mutationTask = _mutationStore
                .ChangeAccessStatusAsync(
                    account.AccountId,
                    AccountAccessStatus.Banned,
                    account.StateRevision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "concurrent-ban"),
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");
            Assert.False(mutationTask.IsCompleted);

            redemptionTask = _redemptionStore
                .TryRedeemAsync(sessionUid, authenticationKey, CancellationToken)
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets", minimumWaitingLockCount: 2);
            Assert.False(redemptionTask.IsCompleted);

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            AccountMutationStatus mutation = await mutationTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );
            GameLoginTicketIdentity? identity = await redemptionTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            Assert.Equal(AccountMutationStatus.Applied, mutation);
            Assert.Null(identity);
            Assert.Equal(
                AccountAccessStatus.Banned,
                (await ReadAccountAsync(account.AccountId)).AccessStatus
            );
            Assert.False(await TicketExistsAsync(sessionUid));
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

            await ObserveTaskAsync(mutationTask);
            await ObserveTaskAsync(redemptionTask);
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenCancelledWaitingForAccountLock_RollsBackWithoutSideEffects()
    {
        AccountRecord account = await InsertAccountAsync();
        uint sessionUid = NextSessionUid();
        uint authenticationKey = NextAuthenticationKey();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        Task<AccountMutationStatus>? mutationTask = null;
        bool gateReleased = false;

        try
        {
            await LockAccountAsync(gateConnection, gateTransaction, account.AccountId);

            mutationTask = _mutationStore
                .ChangeAccessStatusAsync(
                    account.AccountId,
                    AccountAccessStatus.Banned,
                    account.StateRevision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "cancelled-ban"),
                    cancellation.Token
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");
            Assert.False(mutationTask.IsCompleted);

            cancellation.Cancel();

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            );

            AccountRecord persisted = await ReadAccountAsync(account.AccountId);

            Assert.Equal(AccountAccessStatus.Active, persisted.AccessStatus);
            Assert.Equal(1ul, persisted.StateRevision);
            Assert.True(await TicketExistsAsync(sessionUid));
            Assert.Equal(0, await ReadAuditEventCountAsync(account.AccountId));
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

            await ObserveTaskAsync(mutationTask);
        }
    }

    [Fact]
    public async Task ChangeAccessStatus_WhenCancelledWaitingForTicketLock_RollsBackWithoutSideEffects()
    {
        AccountRecord account = await InsertAccountAsync();
        uint sessionUid = NextSessionUid();
        uint authenticationKey = NextAuthenticationKey();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection gateConnection = new(_database.AdministrativeConnectionString);
        await gateConnection.OpenAsync(CancellationToken);
        await using MySqlTransaction gateTransaction = await gateConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        Task<AccountMutationStatus>? mutationTask = null;
        bool gateReleased = false;

        try
        {
            await LockTicketAsync(gateConnection, gateTransaction, sessionUid);

            mutationTask = _mutationStore
                .ChangeAccessStatusAsync(
                    account.AccountId,
                    AccountAccessStatus.Banned,
                    account.StateRevision,
                    AccountMutationContext.ForSystem(Guid.NewGuid(), "cancelled-ban"),
                    cancellation.Token
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");
            Assert.False(mutationTask.IsCompleted);

            cancellation.Cancel();

            await gateTransaction.RollbackAsync(CancellationToken.None);
            gateReleased = true;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await mutationTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            );

            AccountRecord persisted = await ReadAccountAsync(account.AccountId);

            Assert.Equal(AccountAccessStatus.Active, persisted.AccessStatus);
            Assert.Equal(1ul, persisted.StateRevision);
            Assert.True(await TicketExistsAsync(sessionUid));
            Assert.Equal(0, await ReadAuditEventCountAsync(account.AccountId));
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

            await ObserveTaskAsync(mutationTask);
        }
    }

    private async Task<AccountRecord> InsertAccountAsync()
    {
        AccountRecord account = new()
        {
            Username = "Race" + Guid.NewGuid().ToString("N")[..28],
            AccessStatus = AccountAccessStatus.Active,
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

        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        db.Accounts.Add(account);
        await db.SaveChangesAsync(CancellationToken);

        return account;
    }

    private async Task InsertTicketAsync(
        AccountRecord account,
        uint sessionUid,
        uint authenticationKey
    )
    {
        byte[] verifier = _authenticationKeyRing.CreateVerifier(sessionUid, authenticationKey);

        try
        {
            await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(
                CancellationToken
            );
            await using MySqlCommand command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO `game_login_tickets`
                    (`session_uid`,
                     `account_id`,
                     `username`,
                     `issued_at_utc`,
                     `expires_at_utc`,
                     `authentication_key_verifier`,
                     `authentication_key_verifier_key_id`)
                VALUES
                    (@session_uid,
                     @account_id,
                     @username,
                     TIMESTAMPADD(MINUTE, -1, UTC_TIMESTAMP(6)),
                     TIMESTAMPADD(MINUTE, 5, UTC_TIMESTAMP(6)),
                     @authentication_key_verifier,
                     @authentication_key_verifier_key_id);
                """;

            command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;
            command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = account.AccountId;
            command
                .Parameters.Add(
                    "@username",
                    MySqlDbType.VarChar,
                    AccountCredentialPolicy.MaximumUsernameLength
                )
                .Value = account.Username;
            command
                .Parameters.Add(
                    "@authentication_key_verifier",
                    MySqlDbType.Binary,
                    GameLoginTicketAuthenticationKeyVerifier.VerifierSize
                )
                .Value = verifier;
            command
                .Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16)
                .Value = ActiveVerificationKeyId;

            Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }
    }

    private async Task<AccountRecord> ReadAccountAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        return await db
            .Accounts.AsNoTracking()
            .Include(account => account.PasswordCredential)
            .SingleAsync(account => account.AccountId == accountId, CancellationToken);
    }

    private async Task<int> ReadAuditEventCountAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );
        return await db.AccountAuditEvents.CountAsync(
            audit => audit.AccountId == accountId,
            CancellationToken
        );
    }

    private async Task<bool> TicketExistsAsync(uint sessionUid)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(
            CancellationToken
        );
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText =
            "SELECT COUNT(*) FROM `game_login_tickets` WHERE `session_uid` = @session_uid;";
        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        long count = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture
        );
        return count == 1;
    }

    private async Task LockAccountAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint accountId
    )
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "SELECT `account_id` FROM `accounts` WHERE `account_id` = @account_id FOR UPDATE;";
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        Assert.NotNull(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task LockPasswordCredentialAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint accountId
    )
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "SELECT `account_id` FROM `account_password_credentials` WHERE `account_id` = @account_id FOR UPDATE;";
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        Assert.NotNull(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task LockTicketAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint sessionUid
    )
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            "SELECT `session_uid` FROM `game_login_tickets` WHERE `session_uid` = @session_uid FOR UPDATE;";
        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        Assert.NotNull(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task WaitForInnoDbLockWaitAsync(
        string tableName,
        long minimumWaitingLockCount = 1
    )
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

            command.Parameters.Add("@schema_name", MySqlDbType.VarChar).Value =
                AccountDatabaseFixture.DatabaseName;
            command.Parameters.Add("@table_name", MySqlDbType.VarChar).Value = tableName;

            long waitingLockCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(CancellationToken),
                System.Globalization.CultureInfo.InvariantCulture
            );

            if (waitingLockCount >= minimumWaitingLockCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        throw new TimeoutException(
            $"MySQL did not report the expected InnoDB row-lock wait for table '{tableName}'."
        );
    }

    private static async Task ObserveTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch { }
    }

    private static GameLoginTicketGrantRequest CreateGrantRequest(AccountRecord account)
    {
        return new GameLoginTicketGrantRequest(
            account.AccountId,
            account.Username,
            NextSessionUid(),
            NextAuthenticationKey()
        );
    }

    private static uint NextSessionUid()
    {
        uint value = unchecked((uint)Interlocked.Increment(ref s_nextSessionUid));
        return value == 0 ? unchecked((uint)Interlocked.Increment(ref s_nextSessionUid)) : value;
    }

    private static uint NextAuthenticationKey()
    {
        uint value = unchecked((uint)Interlocked.Increment(ref s_nextAuthenticationKey));
        return value == 0
            ? unchecked((uint)Interlocked.Increment(ref s_nextAuthenticationKey))
            : value;
    }
}
