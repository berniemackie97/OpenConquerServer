using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameLoginTicketExpirationCleanerTests : IClassFixture<AccountDatabaseFixture>, IAsyncLifetime
{
    private const string PasswordHash = "$openconquer$ticket-cleanup-test$HashValue";
    private const ushort VerificationKeyId = 1;

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset s_nowUtc = new(2026, 1, 2, 12, 30, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly AccountDatabaseFixture _database;
    private readonly MySqlDataSource _dataSource;
    private readonly GameLoginTicketExpirationCleanerOptions _options;
    private readonly FrozenTimeProvider _timeProvider;
    private readonly GameLoginTicketExpirationCleaner _cleaner;

    public GameLoginTicketExpirationCleanerTests(AccountDatabaseFixture database)
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
        _options = new GameLoginTicketExpirationCleanerOptions();
        _timeProvider = new FrozenTimeProvider(s_nowUtc);
        _cleaner = new GameLoginTicketExpirationCleaner(_dataSource, _options, _timeProvider);
    }

    public async ValueTask InitializeAsync()
    {
        await ClearTicketsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public void Constructor_WhenDataSourceIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketExpirationCleaner(null!, _options, _timeProvider));

        Assert.Equal("dataSource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenOptionsAreNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketExpirationCleaner(_dataSource, null!, _timeProvider));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTimeProviderIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketExpirationCleaner(_dataSource, _options, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_WhenAlreadyCancelled_ThrowsWithoutDeleting()
    {
        AccountRecord account = await InsertAccountAsync();
        uint sessionUid = 100;

        await InsertTicketAsync(account, sessionUid, CleanupCutoffUtc().AddMinutes(-1));

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _cleaner.DeleteExpiredBatchAsync(cancellation.Token).AsTask());

        Assert.True(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_WhenNoTicketHasPassedCleanupCutoff_ReturnsZero()
    {
        AccountRecord account = await InsertAccountAsync();
        DateTimeOffset cutoffUtc = CleanupCutoffUtc();

        await InsertTicketAsync(account, 100, cutoffUtc.AddMilliseconds(1));
        await InsertTicketAsync(account, 200, s_nowUtc.AddMinutes(1));

        int affected = await _cleaner.DeleteExpiredBatchAsync(CancellationToken);

        Assert.Equal(0, affected);
        Assert.True(await TicketExistsAsync(100));
        Assert.True(await TicketExistsAsync(200));
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_DeletesOnlyTicketsAtOrBeforeCleanupCutoff()
    {
        AccountRecord account = await InsertAccountAsync();
        DateTimeOffset cutoffUtc = CleanupCutoffUtc();

        const uint olderSessionUid = 100;
        const uint boundarySessionUid = 200;
        const uint insideGraceSessionUid = 300;
        const uint unexpiredSessionUid = 400;

        await InsertTicketAsync(account, olderSessionUid, cutoffUtc.AddSeconds(-1));
        await InsertTicketAsync(account, boundarySessionUid, cutoffUtc);
        await InsertTicketAsync(account, insideGraceSessionUid, cutoffUtc.AddMilliseconds(1));
        await InsertTicketAsync(account, unexpiredSessionUid, s_nowUtc.AddMinutes(1));

        int affected = await _cleaner.DeleteExpiredBatchAsync(CancellationToken);

        Assert.Equal(2, affected);

        Assert.False(await TicketExistsAsync(olderSessionUid));
        Assert.False(await TicketExistsAsync(boundarySessionUid));
        Assert.True(await TicketExistsAsync(insideGraceSessionUid));
        Assert.True(await TicketExistsAsync(unexpiredSessionUid));
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_EnforcesMaximumBatchSizeAndDeletesOldestTicketsFirst()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicketExpirationCleanerOptions options = new(maximumBatchSize: 2);
        GameLoginTicketExpirationCleaner cleaner = new(_dataSource, options, _timeProvider);

        const uint oldestSessionUid = 100;
        const uint middleSessionUid = 200;
        const uint newestSessionUid = 300;

        await InsertTicketAsync(account, oldestSessionUid, CleanupCutoffUtc().AddMinutes(-3));
        await InsertTicketAsync(account, middleSessionUid, CleanupCutoffUtc().AddMinutes(-2));
        await InsertTicketAsync(account, newestSessionUid, CleanupCutoffUtc().AddMinutes(-1));

        int affected = await cleaner.DeleteExpiredBatchAsync(CancellationToken);

        Assert.Equal(2, affected);

        Assert.False(await TicketExistsAsync(oldestSessionUid));
        Assert.False(await TicketExistsAsync(middleSessionUid));
        Assert.True(await TicketExistsAsync(newestSessionUid));
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_WhenEligibleTicketsShareExpiration_UsesSessionUidAsDeterministicTieBreaker()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicketExpirationCleanerOptions options = new(maximumBatchSize: 2);
        GameLoginTicketExpirationCleaner cleaner = new(_dataSource, options, _timeProvider);

        DateTimeOffset expiration = CleanupCutoffUtc().AddMinutes(-1);

        const uint firstSessionUid = 100;
        const uint secondSessionUid = 200;
        const uint thirdSessionUid = 300;

        await InsertTicketAsync(account, thirdSessionUid, expiration);
        await InsertTicketAsync(account, firstSessionUid, expiration);
        await InsertTicketAsync(account, secondSessionUid, expiration);

        int affected = await cleaner.DeleteExpiredBatchAsync(CancellationToken);

        Assert.Equal(2, affected);

        Assert.False(await TicketExistsAsync(firstSessionUid));
        Assert.False(await TicketExistsAsync(secondSessionUid));
        Assert.True(await TicketExistsAsync(thirdSessionUid));
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_RepeatedBatchesDrainEligibleBacklogWithoutExceedingBatchSize()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicketExpirationCleanerOptions options = new(maximumBatchSize: 2);
        GameLoginTicketExpirationCleaner cleaner = new(_dataSource, options, _timeProvider);

        for (uint sessionUid = 1; sessionUid <= 5; sessionUid++)
        {
            await InsertTicketAsync(account, sessionUid, CleanupCutoffUtc().AddMinutes(-1));
        }

        int firstBatch = await cleaner.DeleteExpiredBatchAsync(CancellationToken);
        int secondBatch = await cleaner.DeleteExpiredBatchAsync(CancellationToken);
        int thirdBatch = await cleaner.DeleteExpiredBatchAsync(CancellationToken);
        int fourthBatch = await cleaner.DeleteExpiredBatchAsync(CancellationToken);

        Assert.Equal(2, firstBatch);
        Assert.Equal(2, secondBatch);
        Assert.Equal(1, thirdBatch);
        Assert.Equal(0, fourthBatch);

        Assert.Equal(0, await CountTicketsAsync());
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_WhenRedemptionContendsForEligibleTicket_CleanupDeletesTicketAndRedemptionRejectsIt()
    {
        AccountRecord account = await InsertAccountAsync();

        const uint sessionUid = 500;
        const uint authenticationKey = 600;
        const ushort verificationKeyId = 7;

        byte[] verificationKey = new byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];
        RandomNumberGenerator.Fill(verificationKey);

        byte[] verifier = GameLoginTicketAuthenticationKeyVerifier.Create(
            verificationKey,
            sessionUid,
            authenticationKey);

        try
        {
            using GameLoginTicketAuthenticationKeyRing authenticationKeyRing = new(
                verificationKeyId,
                [new KeyValuePair<ushort, byte[]>(verificationKeyId, verificationKey)]);

            await InsertTicketAsync(
                account,
                sessionUid,
                CleanupCutoffUtc().AddMinutes(-1),
                verifier,
                verificationKeyId);

            GameLoginTicketRedemptionStore redemptionStore = new(_dataSource, authenticationKeyRing);

            await using MySqlConnection lockConnection = new(_database.AdministrativeConnectionString);
            await lockConnection.OpenAsync(CancellationToken);

            await using MySqlTransaction lockTransaction = await lockConnection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                CancellationToken);

            bool lockTransactionCompleted = false;
            Task<int>? cleanupTask = null;
            Task<GameLoginTicketIdentity?>? redemptionTask = null;

            try
            {
                await LockTicketAsync(lockConnection, lockTransaction, sessionUid);

                cleanupTask = _cleaner.DeleteExpiredBatchAsync(CancellationToken).AsTask();

                redemptionTask = redemptionStore.TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    s_nowUtc,
                    CancellationToken).AsTask();

                await WaitForInnoDbLockWaitAsync("game_login_tickets", minimumWaitingLockCount: 2);

                Assert.False(cleanupTask.IsCompleted);
                Assert.False(redemptionTask.IsCompleted);

                await lockTransaction.RollbackAsync(CancellationToken.None);
                lockTransactionCompleted = true;

                int deleted = await cleanupTask;
                GameLoginTicketIdentity? identity = await redemptionTask;

                Assert.Equal(1, deleted);
                Assert.Null(identity);
                Assert.False(await TicketExistsAsync(sessionUid));
            }
            finally
            {
                if (!lockTransactionCompleted)
                {
                    try
                    {
                        await lockTransaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (InvalidOperationException) { }
                }

                if (cleanupTask is not null)
                {
                    try
                    {
                        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                    }
                    catch { }
                }

                if (redemptionTask is not null)
                {
                    try
                    {
                        await redemptionTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                    }
                    catch { }
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public async Task DeleteExpiredBatchAsync_WhenCancelledWhileWaitingForTicketLock_DoesNotDeleteTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        const uint sessionUid = 500;

        await InsertTicketAsync(account, sessionUid, CleanupCutoffUtc().AddMinutes(-1));

        await using MySqlConnection lockConnection = new(_database.AdministrativeConnectionString);
        await lockConnection.OpenAsync(CancellationToken);

        await using MySqlTransaction lockTransaction = await lockConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken);

        bool lockTransactionCompleted = false;
        Task<int>? cleanupTask = null;

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        try
        {
            await LockTicketAsync(lockConnection, lockTransaction, sessionUid);

            cleanupTask = _cleaner.DeleteExpiredBatchAsync(cancellation.Token).AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");

            Assert.False(cleanupTask.IsCompleted);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            });

            await lockTransaction.RollbackAsync(CancellationToken.None);
            lockTransactionCompleted = true;

            Assert.True(await TicketExistsAsync(sessionUid));
        }
        finally
        {
            if (!lockTransactionCompleted)
            {
                try
                {
                    await lockTransaction.RollbackAsync(CancellationToken.None);
                }
                catch (InvalidOperationException) { }
            }

            if (cleanupTask is not null)
            {
                try
                {
                    await cleanupTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private async Task ClearTicketsAsync()
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = "DELETE FROM `game_login_tickets`;";

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<AccountRecord> InsertAccountAsync()
    {
        AccountRecord account = new()
        {
            Username = Guid.NewGuid().ToString("N"),
            AccessStatus = AccountAccessStatus.Active,
            AuthorityRole = AccountAuthorityRole.Player,
            CreationOperationId = Guid.NewGuid(),
            LastSuccessfulLoginAtUtc = null,
            StateRevision = 1,
            CreatedAtUtc = s_createdAtUtc,
            CreatedByActorKind = AccountActorKind.System,
            CreatedByAccountId = null,
            StateChangedAtUtc = s_createdAtUtc,
            StateChangedByActorKind = AccountActorKind.System,
            StateChangedByAccountId = null,
            DeletedAtUtc = null,
            DeletedByActorKind = null,
            DeletedByAccountId = null,
        };

        AccountPasswordCredentialRecord credential = new()
        {
            PasswordHash = PasswordHash,
            PasswordChangedAtUtc = s_createdAtUtc,
            Revision = 1,
            Account = account,
        };

        account.PasswordCredential = credential;

        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(CancellationToken);

        db.Accounts.Add(account);

        await db.SaveChangesAsync(CancellationToken);

        return account;
    }

    private async Task InsertTicketAsync(AccountRecord account, uint sessionUid, DateTimeOffset expiresAtUtc)
    {
        byte[] verifier = new byte[GameLoginTicketAuthenticationKeyVerifier.VerifierSize];

        try
        {
            RandomNumberGenerator.Fill(verifier);

            await InsertTicketAsync(
                account,
                sessionUid,
                expiresAtUtc,
                verifier,
                VerificationKeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }
    }

    private async Task InsertTicketAsync(
        AccountRecord account,
        uint sessionUid,
        DateTimeOffset expiresAtUtc,
        byte[] authenticationKeyVerifier,
        ushort authenticationKeyVerifierKeyId)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
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
                 @issued_at_utc,
                 @expires_at_utc,
                 @authentication_key_verifier,
                 @authentication_key_verifier_key_id);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = account.AccountId;
        command.Parameters.Add("@username", MySqlDbType.VarChar, AccountCredentialPolicy.MaximumUsernameLength).Value = account.Username;
        command.Parameters.Add("@issued_at_utc", MySqlDbType.DateTime).Value = expiresAtUtc.UtcDateTime.AddMinutes(-5);
        command.Parameters.Add("@expires_at_utc", MySqlDbType.DateTime).Value = expiresAtUtc.UtcDateTime;
        command.Parameters.Add(
            "@authentication_key_verifier",
            MySqlDbType.Binary,
            GameLoginTicketAuthenticationKeyVerifier.VerifierSize).Value = authenticationKeyVerifier;
        command.Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16).Value = authenticationKeyVerifierKeyId;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task LockTicketAsync(MySqlConnection connection, MySqlTransaction transaction, uint sessionUid)
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT `session_uid`
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid
            FOR UPDATE;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
    }

    private async Task WaitForInnoDbLockWaitAsync(string tableName, long minimumWaitingLockCount = 1)
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
                    ON `requesting_lock`.`ENGINE_LOCK_ID`
                        = `waits`.`REQUESTING_ENGINE_LOCK_ID`
                WHERE `requesting_lock`.`OBJECT_SCHEMA` = @schema_name
                  AND `requesting_lock`.`OBJECT_NAME` = @table_name
                  AND `requesting_lock`.`LOCK_STATUS` = 'WAITING';
                """;

            command.Parameters.Add("@schema_name", MySqlDbType.VarChar).Value = AccountDatabaseFixture.DatabaseName;
            command.Parameters.Add("@table_name", MySqlDbType.VarChar).Value = tableName;

            object? result = await command.ExecuteScalarAsync(CancellationToken);

            long waitingLockCount = Convert.ToInt64(
                result,
                System.Globalization.CultureInfo.InvariantCulture);

            if (waitingLockCount >= minimumWaitingLockCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        throw new TimeoutException($"MySQL did not report the expected InnoDB row-lock wait for table '{tableName}'.");
    }

    private async Task<bool> TicketExistsAsync(uint sessionUid)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        object? result = await command.ExecuteScalarAsync(CancellationToken);
        long count = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);

        return count == 1;
    }

    private async Task<long> CountTicketsAsync()
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM `game_login_tickets`;";

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private DateTimeOffset CleanupCutoffUtc()
    {
        return s_nowUtc - _options.ExpirationGrace;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
