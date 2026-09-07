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

public sealed class GameLoginTicketRedemptionStoreTests
    : IClassFixture<AccountDatabaseFixture>,
        IAsyncLifetime
{
    private const ushort ActiveVerificationKeyId = 7;
    private const ushort HistoricalVerificationKeyId = 5;
    private const ushort UnknownVerificationKeyId = 99;

    private const string PasswordHash = "$openconquer$ticket-redemption-test$HashValue";

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime s_issuedAtUtc = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime s_expiresAtUtc = s_issuedAtUtc.AddMinutes(5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly AccountDatabaseFixture _database;
    private readonly MySqlDataSource _dataSource;

    private readonly byte[] _activeVerificationKey;
    private readonly byte[] _historicalVerificationKey;

    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing;
    private readonly GameLoginTicketRedemptionStore _store;

    public GameLoginTicketRedemptionStoreTests(AccountDatabaseFixture database)
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

        _activeVerificationKey = CreateVerificationKey();

        _historicalVerificationKey = CreateVerificationKey();

        _authenticationKeyRing = new GameLoginTicketAuthenticationKeyRing(
            ActiveVerificationKeyId,
            [
                new KeyValuePair<ushort, byte[]>(ActiveVerificationKeyId, _activeVerificationKey),
                new KeyValuePair<ushort, byte[]>(
                    HistoricalVerificationKeyId,
                    _historicalVerificationKey
                ),
            ]
        );

        _store = new GameLoginTicketRedemptionStore(_dataSource, _authenticationKeyRing);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _authenticationKeyRing.Dispose();

        CryptographicOperations.ZeroMemory(_activeVerificationKey);

        CryptographicOperations.ZeroMemory(_historicalVerificationKey);

        await _dataSource.DisposeAsync();
    }

    [Fact]
    public void Constructor_WhenDataSourceIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketRedemptionStore(null!, _authenticationKeyRing)
        );

        Assert.Equal("dataSource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAuthenticationKeyRingIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketRedemptionStore(_dataSource, null!)
        );

        Assert.Equal("authenticationKeyRing", exception.ParamName);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenSessionUidIsZero_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _store.TryRedeemAsync(0, 1, CreateValidAttemptInstant(), CancellationToken).AsTask()
            );

        Assert.Equal("sessionUid", exception.ParamName);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenAuthenticationKeyIsZero_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _store.TryRedeemAsync(1, 0, CreateValidAttemptInstant(), CancellationToken).AsTask()
            );

        Assert.Equal("authenticationKey", exception.ParamName);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenAlreadyCancelled_ThrowsWithoutConsumingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _store
                .TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    CreateValidAttemptInstant(),
                    cancellation.Token
                )
                .AsTask()
        );

        Assert.True(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenSessionUidDoesNotExist_ReturnsNull()
    {
        GameLoginTicketIdentity? identity = await _store.TryRedeemAsync(
            GenerateNonzeroUInt32(),
            GenerateNonzeroUInt32(),
            CreateValidAttemptInstant(),
            CancellationToken
        );

        Assert.Null(identity);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenCredentialsAreCorrect_ConsumesTicketExactlyOnce()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        GameLoginTicketIdentity? identity = await _store.TryRedeemAsync(
            sessionUid,
            authenticationKey,
            CreateValidAttemptInstant(),
            CancellationToken
        );

        GameLoginTicketIdentity redeemedIdentity = Assert.IsType<GameLoginTicketIdentity>(identity);

        Assert.Equal(account.AccountId, redeemedIdentity.AccountId);

        Assert.Equal(account.Username, redeemedIdentity.Username);

        Assert.Equal(sessionUid, redeemedIdentity.SessionUid);

        Assert.False(await TicketExistsAsync(sessionUid));

        GameLoginTicketIdentity? replay = await _store.TryRedeemAsync(
            sessionUid,
            authenticationKey,
            CreateValidAttemptInstant(),
            CancellationToken
        );

        Assert.Null(replay);
    }

    [Fact]
    public async Task TryRedeemAsync_WhenAuthenticationKeyIsWrong_DoesNotConsumeTicketAndCorrectKeyStillSucceeds()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        uint wrongAuthenticationKey;

        do
        {
            wrongAuthenticationKey = GenerateNonzeroUInt32();
        } while (wrongAuthenticationKey == authenticationKey);

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        GameLoginTicketIdentity? rejected = await _store.TryRedeemAsync(
            sessionUid,
            wrongAuthenticationKey,
            CreateValidAttemptInstant(),
            CancellationToken
        );

        Assert.Null(rejected);

        Assert.True(await TicketExistsAsync(sessionUid));

        GameLoginTicketIdentity? accepted = await _store.TryRedeemAsync(
            sessionUid,
            authenticationKey,
            CreateValidAttemptInstant(),
            CancellationToken
        );

        Assert.NotNull(accepted);

        Assert.False(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenAttemptOccursAtExpirationBoundary_ReturnsNullWithoutConsumingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        DateTimeOffset expirationBoundary = new(s_expiresAtUtc);

        GameLoginTicketIdentity? identity = await _store.TryRedeemAsync(
            sessionUid,
            authenticationKey,
            expirationBoundary,
            CancellationToken
        );

        Assert.Null(identity);

        Assert.True(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenTicketUsesHistoricalVerificationKey_RedeemsSuccessfully()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        byte[] verifier = GameLoginTicketAuthenticationKeyVerifier.Create(
            _historicalVerificationKey,
            sessionUid,
            authenticationKey
        );

        try
        {
            await InsertProtectedTicketAsync(
                account.AccountId,
                account.Username,
                sessionUid,
                verifier,
                HistoricalVerificationKeyId
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }

        GameLoginTicketIdentity? identity = await _store.TryRedeemAsync(
            sessionUid,
            authenticationKey,
            CreateValidAttemptInstant(),
            CancellationToken
        );

        GameLoginTicketIdentity redeemedIdentity = Assert.IsType<GameLoginTicketIdentity>(identity);

        Assert.Equal(account.AccountId, redeemedIdentity.AccountId);

        Assert.Equal(sessionUid, redeemedIdentity.SessionUid);

        Assert.False(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenVerifierKeyIdIsUnknown_FailsClosedWithoutConsumingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        byte[] verifier = new byte[GameLoginTicketAuthenticationKeyVerifier.VerifierSize];

        RandomNumberGenerator.Fill(verifier);

        try
        {
            await InsertProtectedTicketAsync(
                account.AccountId,
                account.Username,
                sessionUid,
                verifier,
                UnknownVerificationKeyId
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _store
                .TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    CreateValidAttemptInstant(),
                    CancellationToken
                )
                .AsTask()
        );

        Assert.True(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenPersistedUsernameIsInvalid_FailsClosedWithoutConsumingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        byte[] verifier = _authenticationKeyRing.CreateVerifier(sessionUid, authenticationKey);

        try
        {
            await InsertProtectedTicketAsync(
                account.AccountId,
                string.Empty,
                sessionUid,
                verifier,
                ActiveVerificationKeyId
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store
                .TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    CreateValidAttemptInstant(),
                    CancellationToken
                )
                .AsTask()
        );

        Assert.True(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_ConcurrentCorrectAttemptsProduceExactlyOneWinner()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        Task<GameLoginTicketIdentity?>[] operations = Enumerable
            .Range(0, 8)
            .Select(_ =>
                _store
                    .TryRedeemAsync(
                        sessionUid,
                        authenticationKey,
                        CreateValidAttemptInstant(),
                        CancellationToken
                    )
                    .AsTask()
            )
            .ToArray();

        GameLoginTicketIdentity?[] results = await Task.WhenAll(operations);

        GameLoginTicketIdentity[] winners = results.OfType<GameLoginTicketIdentity>().ToArray();

        Assert.Single(winners);

        Assert.Equal(account.AccountId, winners[0].AccountId);

        Assert.Equal(account.Username, winners[0].Username);

        Assert.Equal(sessionUid, winners[0].SessionUid);

        Assert.Equal(operations.Length - 1, results.Count(identity => identity is null));

        Assert.False(await TicketExistsAsync(sessionUid));
    }

    [Fact]
    public async Task TryRedeemAsync_WhenWrongAndCorrectAttemptsContendForSameTicket_CorrectAttemptWins()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        uint wrongAuthenticationKey;

        do
        {
            wrongAuthenticationKey = GenerateNonzeroUInt32();
        } while (wrongAuthenticationKey == authenticationKey);

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection lockConnection = new(_database.AdministrativeConnectionString);

        await lockConnection.OpenAsync(CancellationToken);

        await using MySqlTransaction lockTransaction = await lockConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        bool lockTransactionCompleted = false;

        Task<GameLoginTicketIdentity?>? wrongAttempt = null;

        Task<GameLoginTicketIdentity?>? correctAttempt = null;

        try
        {
            await LockTicketAsync(lockConnection, lockTransaction, sessionUid);

            wrongAttempt = _store
                .TryRedeemAsync(
                    sessionUid,
                    wrongAuthenticationKey,
                    CreateValidAttemptInstant(),
                    CancellationToken
                )
                .AsTask();

            correctAttempt = _store
                .TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    CreateValidAttemptInstant(),
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets", minimumWaitingLockCount: 2);

            Assert.False(wrongAttempt.IsCompleted);

            Assert.False(correctAttempt.IsCompleted);

            await lockTransaction.RollbackAsync(CancellationToken.None);

            lockTransactionCompleted = true;

            GameLoginTicketIdentity?[] results = await Task.WhenAll(wrongAttempt, correctAttempt);

            Assert.Null(results[0]);

            GameLoginTicketIdentity accepted = Assert.IsType<GameLoginTicketIdentity>(results[1]);

            Assert.Equal(account.AccountId, accepted.AccountId);

            Assert.Equal(account.Username, accepted.Username);

            Assert.Equal(sessionUid, accepted.SessionUid);

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

            if (wrongAttempt is not null)
            {
                try
                {
                    await wrongAttempt.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
                catch { }
            }

            if (correctAttempt is not null)
            {
                try
                {
                    await correctAttempt.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        CancellationToken.None
                    );
                }
                catch { }
            }
        }
    }

    [Fact]
    public async Task TryRedeemAsync_WhenCancelledWhileWaitingForTicketLock_DoesNotConsumeTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        uint authenticationKey = GenerateNonzeroUInt32();

        await InsertTicketAsync(account, sessionUid, authenticationKey);

        await using MySqlConnection lockConnection = new(_database.AdministrativeConnectionString);

        await lockConnection.OpenAsync(CancellationToken);

        await using MySqlTransaction lockTransaction = await lockConnection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            CancellationToken
        );

        bool lockTransactionCompleted = false;

        Task<GameLoginTicketIdentity?>? redemptionTask = null;

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        try
        {
            await LockTicketAsync(lockConnection, lockTransaction, sessionUid);

            redemptionTask = _store
                .TryRedeemAsync(
                    sessionUid,
                    authenticationKey,
                    CreateValidAttemptInstant(),
                    cancellation.Token
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("game_login_tickets");

            Assert.False(redemptionTask.IsCompleted);

            cancellation.Cancel();

            await lockTransaction.RollbackAsync(CancellationToken.None);

            lockTransactionCompleted = true;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await redemptionTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            });

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

            if (redemptionTask is not null)
            {
                try
                {
                    await redemptionTask.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        CancellationToken.None
                    );
                }
                catch { }
            }
        }
    }

    private async Task<AccountRecord> InsertAccountAsync()
    {
        string username = CreateUsername();

        AccountRecord account = new()
        {
            Username = username,
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
            await InsertProtectedTicketAsync(
                account.AccountId,
                account.Username,
                sessionUid,
                verifier,
                ActiveVerificationKeyId
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifier);
        }
    }

    private async Task InsertProtectedTicketAsync(
        uint accountId,
        string username,
        uint sessionUid,
        byte[] authenticationKeyVerifier,
        ushort authenticationKeyVerifierKeyId
    )
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
                 @issued_at_utc,
                 @expires_at_utc,
                 @authentication_key_verifier,
                 @authentication_key_verifier_key_id);
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        command
            .Parameters.Add(
                "@username",
                MySqlDbType.VarChar,
                AccountCredentialPolicy.MaximumUsernameLength
            )
            .Value = username;

        command.Parameters.Add("@issued_at_utc", MySqlDbType.DateTime).Value = s_issuedAtUtc;

        command.Parameters.Add("@expires_at_utc", MySqlDbType.DateTime).Value = s_expiresAtUtc;

        command
            .Parameters.Add(
                "@authentication_key_verifier",
                MySqlDbType.Binary,
                GameLoginTicketAuthenticationKeyVerifier.VerifierSize
            )
            .Value = authenticationKeyVerifier;

        command.Parameters.Add("@authentication_key_verifier_key_id", MySqlDbType.UInt16).Value =
            authenticationKeyVerifierKeyId;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task<bool> TicketExistsAsync(uint sessionUid)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(
            CancellationToken
        );

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

    private async Task LockTicketAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint sessionUid
    )
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
                    ON `requesting_lock`.`ENGINE_LOCK_ID`
                        = `waits`.`REQUESTING_ENGINE_LOCK_ID`
                WHERE `requesting_lock`.`OBJECT_SCHEMA` = @schema_name
                  AND `requesting_lock`.`OBJECT_NAME` = @table_name
                  AND `requesting_lock`.`LOCK_STATUS` = 'WAITING';
                """;

            command.Parameters.Add("@schema_name", MySqlDbType.VarChar).Value =
                AccountDatabaseFixture.DatabaseName;

            command.Parameters.Add("@table_name", MySqlDbType.VarChar).Value = tableName;

            object? result = await command.ExecuteScalarAsync(CancellationToken);

            long waitingLockCount = Convert.ToInt64(
                result,
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

    private static DateTimeOffset CreateValidAttemptInstant()
    {
        return new DateTimeOffset(s_issuedAtUtc.AddMinutes(1));
    }

    private static byte[] CreateVerificationKey()
    {
        byte[] verificationKey = new byte[
            GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize
        ];

        RandomNumberGenerator.Fill(verificationKey);

        return verificationKey;
    }

    private static uint GenerateNonzeroUInt32()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];

        try
        {
            uint value;

            do
            {
                RandomNumberGenerator.Fill(bytes);

                value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            } while (value == 0);

            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string CreateUsername()
    {
        return "User" + Guid.NewGuid().ToString("N")[..28];
    }
}
