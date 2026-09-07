using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Security;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameLoginTicketGrantStoreTests
    : IClassFixture<AccountDatabaseFixture>,
        IAsyncLifetime
{
    private const ushort ActiveVerificationKeyId = 7;

    private const string PasswordHash = "$openconquer$ticket-grant-test$HashValue";

    private static readonly DateTime s_createdAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private readonly AccountDatabaseFixture _database;
    private readonly MySqlDataSource _dataSource;
    private readonly GameLoginTicketAuthenticationKeyRing _authenticationKeyRing;
    private readonly GameLoginTicketGrantStore _store;

    public GameLoginTicketGrantStoreTests(AccountDatabaseFixture database)
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

        _authenticationKeyRing = CreateAuthenticationKeyRing();

        _store = new GameLoginTicketGrantStore(_dataSource, _authenticationKeyRing);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _authenticationKeyRing.Dispose();

        await _dataSource.DisposeAsync();
    }

    [Fact]
    public void Constructor_WhenDataSourceIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketGrantStore(null!, _authenticationKeyRing)
        );

        Assert.Equal("dataSource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAuthenticationKeyRingIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new GameLoginTicketGrantStore(_dataSource, null!)
        );

        Assert.Equal("authenticationKeyRing", exception.ParamName);
    }

    [Fact]
    public async Task TryGrantAsync_WhenTicketIsNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store
                .TryGrantAsync(
                    null!,
                    expectedAccountStateRevision: 1,
                    expectedPasswordCredentialRevision: 1,
                    CancellationToken
                )
                .AsTask()
        );

        Assert.Equal("ticket", exception.ParamName);
    }

    [Fact]
    public async Task TryGrantAsync_WhenExpectedAccountStateRevisionIsZero_ThrowsArgumentOutOfRangeException()
    {
        GameLoginTicket ticket = CreateTicket(accountId: 1, username: "TicketUser");

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _store
                    .TryGrantAsync(
                        ticket,
                        expectedAccountStateRevision: 0,
                        expectedPasswordCredentialRevision: 1,
                        CancellationToken
                    )
                    .AsTask()
            );

        Assert.Equal("expectedAccountStateRevision", exception.ParamName);
    }

    [Fact]
    public async Task TryGrantAsync_WhenExpectedPasswordCredentialRevisionIsZero_ThrowsArgumentOutOfRangeException()
    {
        GameLoginTicket ticket = CreateTicket(accountId: 1, username: "TicketUser");

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _store
                    .TryGrantAsync(
                        ticket,
                        expectedAccountStateRevision: 1,
                        expectedPasswordCredentialRevision: 0,
                        CancellationToken
                    )
                    .AsTask()
            );

        Assert.Equal("expectedPasswordCredentialRevision", exception.ParamName);
    }

    [Fact]
    public async Task TryGrantAsync_WhenAlreadyCancelled_ThrowsWithoutPersistingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _store
                .TryGrantAsync(
                    ticket,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    cancellation.Token
                )
                .AsTask()
        );

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenAuthenticationStateMatches_PersistsProtectedTicketDurably()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.Granted, status);

        PersistedTicket persisted = Assert.IsType<PersistedTicket>(
            await ReadTicketAsync(ticket.SessionUid)
        );

        Assert.Equal(ticket.SessionUid, persisted.SessionUid);

        Assert.Equal(ticket.AccountId, persisted.AccountId);

        Assert.Equal(ticket.Username, persisted.Username);

        Assert.Equal(ticket.IssuedAtUtc.UtcDateTime, persisted.IssuedAtUtc);

        Assert.Equal(ticket.ExpiresAtUtc.UtcDateTime, persisted.ExpiresAtUtc);

        Assert.Equal(ActiveVerificationKeyId, persisted.AuthenticationKeyVerifierKeyId);

        Assert.Equal(
            GameLoginTicketAuthenticationKeyVerifier.VerifierSize,
            persisted.AuthenticationKeyVerifier.Length
        );

        Assert.True(
            _authenticationKeyRing.Verify(
                persisted.AuthenticationKeyVerifierKeyId,
                persisted.AuthenticationKeyVerifier,
                ticket.SessionUid,
                ticket.AuthenticationKey
            )
        );
    }

    [Theory]
    [InlineData(AccountAccessStatus.Suspended)]
    [InlineData(AccountAccessStatus.Banned)]
    public async Task TryGrantAsync_WhenAccountIsNotActive_ReturnsAuthenticationStateChanged(
        AccountAccessStatus accessStatus
    )
    {
        AccountRecord account = await InsertAccountAsync(accessStatus);

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenAccountIsDeleted_ReturnsAuthenticationStateChanged()
    {
        AccountRecord account = await InsertAccountAsync();

        await DeleteAccountAsync(account.AccountId);

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            expectedAccountStateRevision: account.StateRevision,
            expectedPasswordCredentialRevision: account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenAccountStateRevisionIsStale_ReturnsAuthenticationStateChanged()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            checked(account.StateRevision + 1),
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenPasswordCredentialRevisionIsStale_ReturnsAuthenticationStateChanged()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            account.StateRevision,
            checked(account.PasswordCredential.Revision + 1),
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenCanonicalUsernameDoesNotMatchExactly_ReturnsAuthenticationStateChanged()
    {
        AccountRecord account = await InsertAccountAsync();

        string differentUsername = account.Username.ToUpperInvariant();

        Assert.NotEqual(account.Username, differentUsername);

        GameLoginTicket ticket = CreateTicket(account.AccountId, differentUsername);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenAccountDoesNotExist_ReturnsAuthenticationStateChanged()
    {
        GameLoginTicket ticket = CreateTicket(uint.MaxValue, "MissingAccount");

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            expectedAccountStateRevision: 1,
            expectedPasswordCredentialRevision: 1,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenPasswordCredentialDoesNotExist_ReturnsAuthenticationStateChanged()
    {
        AccountRecord account = await InsertAccountAsync();

        await RemovePasswordCredentialAdministrativelyAsync(account.AccountId);

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        GameLoginTicketGrantStatus status = await _store.TryGrantAsync(
            ticket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

        Assert.Null(await ReadTicketAsync(ticket.SessionUid));
    }

    [Fact]
    public async Task TryGrantAsync_WhenSessionUidAlreadyExists_ReturnsCollisionWithoutReplacingExistingTicket()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        GameLoginTicket originalTicket = CreateTicket(
            account.AccountId,
            account.Username,
            sessionUid,
            authenticationKey: 0x1020_3040u
        );

        GameLoginTicket collidingTicket = CreateTicket(
            account.AccountId,
            account.Username,
            sessionUid,
            authenticationKey: 0x5060_7080u
        );

        GameLoginTicketGrantStatus originalStatus = await _store.TryGrantAsync(
            originalTicket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        GameLoginTicketGrantStatus collisionStatus = await _store.TryGrantAsync(
            collidingTicket,
            account.StateRevision,
            account.PasswordCredential.Revision,
            CancellationToken
        );

        Assert.Equal(GameLoginTicketGrantStatus.Granted, originalStatus);

        Assert.Equal(GameLoginTicketGrantStatus.SessionUidCollision, collisionStatus);

        PersistedTicket persisted = Assert.IsType<PersistedTicket>(
            await ReadTicketAsync(sessionUid)
        );

        Assert.True(
            _authenticationKeyRing.Verify(
                persisted.AuthenticationKeyVerifierKeyId,
                persisted.AuthenticationKeyVerifier,
                sessionUid,
                originalTicket.AuthenticationKey
            )
        );

        Assert.False(
            _authenticationKeyRing.Verify(
                persisted.AuthenticationKeyVerifierKeyId,
                persisted.AuthenticationKeyVerifier,
                sessionUid,
                collidingTicket.AuthenticationKey
            )
        );
    }

    [Fact]
    public async Task TryGrantAsync_ConcurrentSameSessionUidHasExactlyOneWinner()
    {
        AccountRecord account = await InsertAccountAsync();

        uint sessionUid = GenerateNonzeroUInt32();

        GameLoginTicket[] tickets = Enumerable
            .Range(0, 8)
            .Select(index =>
                CreateTicket(
                    account.AccountId,
                    account.Username,
                    sessionUid,
                    checked(0x7000_0001u + (uint)index)
                )
            )
            .ToArray();

        Task<GameLoginTicketGrantStatus>[] operations = tickets
            .Select(ticket =>
                _store
                    .TryGrantAsync(
                        ticket,
                        account.StateRevision,
                        account.PasswordCredential.Revision,
                        CancellationToken
                    )
                    .AsTask()
            )
            .ToArray();

        GameLoginTicketGrantStatus[] results = await Task.WhenAll(operations);

        Assert.Single(results, status => status == GameLoginTicketGrantStatus.Granted);

        Assert.Equal(
            tickets.Length - 1,
            results.Count(status => status == GameLoginTicketGrantStatus.SessionUidCollision)
        );

        int winnerIndex = Array.FindIndex(
            results,
            status => status == GameLoginTicketGrantStatus.Granted
        );

        Assert.InRange(winnerIndex, 0, tickets.Length - 1);

        PersistedTicket persisted = Assert.IsType<PersistedTicket>(
            await ReadTicketAsync(sessionUid)
        );

        for (int index = 0; index < tickets.Length; index++)
        {
            bool verified = _authenticationKeyRing.Verify(
                persisted.AuthenticationKeyVerifierKeyId,
                persisted.AuthenticationKeyVerifier,
                sessionUid,
                tickets[index].AuthenticationKey
            );

            Assert.Equal(index == winnerIndex, verified);
        }
    }

    [Fact]
    public async Task TryGrantAsync_WhenConcurrentAccountStateMutationCommits_RejectsStaleAuthentication()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        await using MySqlConnection mutationConnection = new(
            _database.AdministrativeConnectionString
        );

        await mutationConnection.OpenAsync(CancellationToken);

        await using MySqlTransaction mutationTransaction =
            await mutationConnection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                CancellationToken
            );

        bool transactionCompleted = false;
        Task<GameLoginTicketGrantStatus>? grantTask = null;

        try
        {
            await using (MySqlCommand mutation = mutationConnection.CreateCommand())
            {
                mutation.Transaction = mutationTransaction;
                mutation.CommandText = """
                    UPDATE `accounts`
                    SET
                        `access_status` = @access_status,
                        `state_revision` = `state_revision` + 1,
                        `state_changed_at_utc` = @state_changed_at_utc,
                        `state_changed_by_actor_kind` = @state_changed_by_actor_kind,
                        `state_changed_by_account_id` = NULL
                    WHERE `account_id` = @account_id;
                    """;

                mutation.Parameters.Add("@access_status", MySqlDbType.UByte).Value = (byte)
                    AccountAccessStatus.Suspended;

                mutation.Parameters.Add("@state_changed_at_utc", MySqlDbType.DateTime).Value =
                    s_createdAtUtc.AddMinutes(1);

                mutation.Parameters.Add("@state_changed_by_actor_kind", MySqlDbType.UByte).Value =
                    (byte)AccountActorKind.System;

                mutation.Parameters.Add("@account_id", MySqlDbType.UInt32).Value =
                    account.AccountId;

                int affected = await mutation.ExecuteNonQueryAsync(CancellationToken);

                Assert.Equal(1, affected);
            }

            grantTask = _store
                .TryGrantAsync(
                    ticket,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");

            Assert.False(grantTask.IsCompleted);

            await mutationTransaction.CommitAsync(CancellationToken.None);

            transactionCompleted = true;

            GameLoginTicketGrantStatus status = await grantTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

            Assert.Null(await ReadTicketAsync(ticket.SessionUid));
        }
        finally
        {
            if (!transactionCompleted)
            {
                try
                {
                    await mutationTransaction.RollbackAsync(CancellationToken.None);
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
        }
    }

    [Fact]
    public async Task TryGrantAsync_WhenConcurrentPasswordCredentialMutationCommits_RejectsStaleAuthentication()
    {
        AccountRecord account = await InsertAccountAsync();

        GameLoginTicket ticket = CreateTicket(account.AccountId, account.Username);

        await using MySqlConnection mutationConnection = new(
            _database.AdministrativeConnectionString
        );

        await mutationConnection.OpenAsync(CancellationToken);

        await using MySqlTransaction mutationTransaction =
            await mutationConnection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                CancellationToken
            );

        bool transactionCompleted = false;
        Task<GameLoginTicketGrantStatus>? grantTask = null;

        try
        {
            await LockAccountAsync(mutationConnection, mutationTransaction, account.AccountId);

            await using (MySqlCommand mutation = mutationConnection.CreateCommand())
            {
                mutation.Transaction = mutationTransaction;
                mutation.CommandText = """
                    UPDATE `account_password_credentials`
                    SET
                        `revision` = `revision` + 1,
                        `password_changed_at_utc` = @password_changed_at_utc
                    WHERE `account_id` = @account_id;
                    """;

                mutation.Parameters.Add("@password_changed_at_utc", MySqlDbType.DateTime).Value =
                    s_createdAtUtc.AddMinutes(1);

                mutation.Parameters.Add("@account_id", MySqlDbType.UInt32).Value =
                    account.AccountId;

                int affected = await mutation.ExecuteNonQueryAsync(CancellationToken);

                Assert.Equal(1, affected);
            }

            grantTask = _store
                .TryGrantAsync(
                    ticket,
                    account.StateRevision,
                    account.PasswordCredential.Revision,
                    CancellationToken
                )
                .AsTask();

            await WaitForInnoDbLockWaitAsync("accounts");

            Assert.False(grantTask.IsCompleted);

            await mutationTransaction.CommitAsync(CancellationToken.None);

            transactionCompleted = true;

            GameLoginTicketGrantStatus status = await grantTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                CancellationToken
            );

            Assert.Equal(GameLoginTicketGrantStatus.AuthenticationStateChanged, status);

            Assert.Null(await ReadTicketAsync(ticket.SessionUid));
        }
        finally
        {
            if (!transactionCompleted)
            {
                try
                {
                    await mutationTransaction.RollbackAsync(CancellationToken.None);
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
        }
    }

    private async Task LockAccountAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint accountId
    )
    {
        await using MySqlCommand command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT `account_id`
            FROM `accounts`
            WHERE `account_id` = @account_id
            FOR UPDATE;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
    }

    private async Task WaitForInnoDbLockWaitAsync(string tableName)
    {
        const int maximumPollingAttempts = 500;

        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);

        await connection.OpenAsync(CancellationToken);

        for (int attempt = 0; attempt < maximumPollingAttempts; attempt++)
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

            if (waitingLockCount > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }

        throw new TimeoutException(
            $"MySQL did not report the expected InnoDB row-lock wait for table '{tableName}'."
        );
    }

    private async Task<AccountRecord> InsertAccountAsync(
        AccountAccessStatus accessStatus = AccountAccessStatus.Active
    )
    {
        string username = CreateUsername();

        AccountRecord account = new()
        {
            Username = username,
            AccessStatus = accessStatus,
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

    private async Task DeleteAccountAsync(uint accountId)
    {
        await using AccountDbContext db = await _database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        AccountRecord account = await db.Accounts.SingleAsync(
            candidate => candidate.AccountId == accountId,
            CancellationToken
        );

        DateTime deletedAtUtc = s_createdAtUtc.AddMinutes(1);

        account.StateRevision++;
        account.StateChangedAtUtc = deletedAtUtc;
        account.StateChangedByActorKind = AccountActorKind.System;
        account.StateChangedByAccountId = null;
        account.DeletedAtUtc = deletedAtUtc;
        account.DeletedByActorKind = AccountActorKind.System;
        account.DeletedByAccountId = null;

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task RemovePasswordCredentialAdministrativelyAsync(uint accountId)
    {
        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM `account_password_credentials`
            WHERE `account_id` = @account_id;
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private async Task<PersistedTicket?> ReadTicketAsync(uint sessionUid)
    {
        await using MySqlConnection connection = await _dataSource.OpenConnectionAsync(
            CancellationToken
        );

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                `session_uid`,
                `account_id`,
                `username`,
                `issued_at_utc`,
                `expires_at_utc`,
                `authentication_key_verifier`,
                `authentication_key_verifier_key_id`
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid;
            """;

        command.Parameters.Add("@session_uid", MySqlDbType.UInt32).Value = sessionUid;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        if (!await reader.ReadAsync(CancellationToken))
        {
            return null;
        }

        PersistedTicket ticket = new(
            reader.GetUInt32(0),
            reader.GetUInt32(1),
            reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetDateTime(4),
            reader.GetFieldValue<byte[]>(5),
            reader.GetUInt16(6)
        );

        Assert.False(await reader.ReadAsync(CancellationToken));

        return ticket;
    }

    private static GameLoginTicket CreateTicket(
        uint accountId,
        string username,
        uint? sessionUid = null,
        uint? authenticationKey = null
    )
    {
        DateTimeOffset issuedAtUtc = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

        return new GameLoginTicket(
            accountId,
            username,
            sessionUid ?? GenerateNonzeroUInt32(),
            authenticationKey ?? GenerateNonzeroUInt32(),
            issuedAtUtc,
            issuedAtUtc.AddMinutes(5)
        );
    }

    private static GameLoginTicketAuthenticationKeyRing CreateAuthenticationKeyRing()
    {
        byte[] verificationKey = new byte[
            GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize
        ];

        RandomNumberGenerator.Fill(verificationKey);

        try
        {
            return new GameLoginTicketAuthenticationKeyRing(
                ActiveVerificationKeyId,
                [new KeyValuePair<ushort, byte[]>(ActiveVerificationKeyId, verificationKey)]
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
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

    private sealed class PersistedTicket(
        uint sessionUid,
        uint accountId,
        string username,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        byte[] authenticationKeyVerifier,
        ushort authenticationKeyVerifierKeyId
    )
    {
        public uint SessionUid { get; } = sessionUid;

        public uint AccountId { get; } = accountId;

        public string Username { get; } = username;

        public DateTime IssuedAtUtc { get; } = issuedAtUtc;

        public DateTime ExpiresAtUtc { get; } = expiresAtUtc;

        public byte[] AuthenticationKeyVerifier { get; } = authenticationKeyVerifier;

        public ushort AuthenticationKeyVerifierKeyId { get; } = authenticationKeyVerifierKeyId;
    }
}
