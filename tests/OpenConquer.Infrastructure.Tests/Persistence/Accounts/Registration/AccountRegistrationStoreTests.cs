using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.Authentication.State;
using OpenConquer.Application.Accounts.Registration;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Registration;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.Registration;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Registration;

/// <summary>
/// Runs the store through a registration identity that holds only the grants the operations guide documents.
/// </summary>
public sealed class AccountRegistrationStoreTests : IClassFixture<AccountDatabaseFixture>, IAsyncLifetime
{
    private const string RegistrationUserName = "openconquer_registration_tests";
    private const string PasswordHash = "$test$registration$hash";

    private readonly AccountDatabaseFixture _database;
    private AccountRegistrationStore? _store;
    private string _registrationConnectionString = string.Empty;

    public AccountRegistrationStoreTests(AccountDatabaseFixture database)
    {
        _database = database;
    }

    private string AdministrativeConnectionString => new MySqlConnectionStringBuilder(_database.AdministrativeConnectionString)
    {
        GuidFormat = MySqlGuidFormat.Binary16,
    }.ConnectionString;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private AccountRegistrationStore Store => _store ?? throw new InvalidOperationException("The store is not initialized.");

    public async ValueTask InitializeAsync()
    {
        string password = Guid.NewGuid().ToString("N");

        await using MySqlConnection connection = new(AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);

        foreach (string statement in new[]
        {
            $"DROP USER IF EXISTS '{RegistrationUserName}'@'%'",
            $"CREATE USER '{RegistrationUserName}'@'%' IDENTIFIED BY '{password}'",
            $"GRANT SELECT, INSERT ON `{AccountDatabaseFixture.DatabaseName}`.`accounts` TO '{RegistrationUserName}'@'%'",
            $"GRANT INSERT ON `{AccountDatabaseFixture.DatabaseName}`.`account_password_credentials` TO '{RegistrationUserName}'@'%'",
            $"GRANT INSERT ON `{AccountDatabaseFixture.DatabaseName}`.`account_audit_events` TO '{RegistrationUserName}'@'%'",
        })
        {
            await using MySqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        MySqlConnectionStringBuilder registration = new(AdministrativeConnectionString)
        {
            UserID = RegistrationUserName,
            Password = password,
        };

        _registrationConnectionString = registration.ConnectionString;
        _store = new AccountRegistrationStore(_registrationConnectionString);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("account_password_credentials")]
    [InlineData("account_audit_events")]
    public async Task TryCreateAsync_LaterInsertFailureRollsBackAndRedactsDriverMessage(string table)
    {
        string username = UniqueName();
        Guid operationId = Guid.NewGuid();
        await ExecuteAsync($"CREATE TRIGGER registration_failure BEFORE INSERT ON `{table}` FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'private-user-and-password';");
        try
        {
            AccountRegistrationPersistenceException failure = await Assert.ThrowsAsync<AccountRegistrationPersistenceException>(() =>
                Store.TryCreateAsync(username, PasswordHash, operationId, CancellationToken).AsTask());
            Assert.False(failure.CommitOutcomeUnknown);
            Assert.Equal(1644, failure.DatabaseErrorNumber);
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain("private-user-and-password", failure.ToString());
            Assert.DoesNotContain(username, failure.ToString());
            Assert.DoesNotContain(PasswordHash, failure.ToString());
            await AssertOperationAbsentAsync(operationId);
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER registration_failure;");
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task TryCreateAsync_LostCommitExchangeOnlyReportsSuccessWithPositiveEvidence(bool commitReachedServer, bool recoveryUnavailable)
    {
        MySqlConnectionStringBuilder settings = new(_registrationConnectionString);
        await using RegistrationCommitFaultProxy proxy = new(settings.Server, (int)settings.Port, commitReachedServer, recoveryUnavailable);
        settings.Server = "127.0.0.1";
        settings.Port = (uint)proxy.Port;
        settings.SslMode = MySqlSslMode.None;
        settings.AllowPublicKeyRetrieval = true;
        settings.Pooling = false;
        AccountRegistrationStore store = new(settings.ConnectionString);
        Guid operation = Guid.NewGuid();
        if (commitReachedServer && !recoveryUnavailable)
        {
            Assert.NotNull(await store.TryCreateAsync(UniqueName(), PasswordHash, operation, CancellationToken));
            Assert.True(proxy.CommitAcknowledged);
        }
        else
        {
            AccountRegistrationPersistenceException failure = await Assert.ThrowsAsync<AccountRegistrationPersistenceException>(() =>
                store.TryCreateAsync(UniqueName(), PasswordHash, operation, CancellationToken).AsTask());
            Assert.True(failure.CommitOutcomeUnknown);
            Assert.Null(failure.InnerException);
            if (!commitReachedServer)
            {
                await AssertOperationAbsentAsync(operation);
            }
            else
            {
                Assert.True(proxy.CommitAcknowledged);
            }
        }

        Assert.Equal(1, proxy.InsertCount);
    }

    [Fact]
    public async Task RegisterAsync_CreatedAccountCanAuthenticateThroughTheProductionRepository()
    {
        AccountPasswordHasher hasher = new();
        AccountRegistrationProtection limiter = new(new AccountRegistrationProtectionOptions(3, TimeSpan.FromHours(1), 2, 100), TimeProvider.System);
        AccountRegistrationService registration = new(Store, hasher, limiter);
        string username = UniqueName();
        AccountRegistrationResult created = await registration.RegisterAsync(username, "Password123".AsMemory(), IPAddress.Loopback, CancellationToken);
        Assert.Equal(AccountRegistrationStatus.Created, created.Status);

        AccountAuthenticationProtection protection = new(new AccountAuthenticationProtectionOptions(), TimeProvider.System);
        AccountAuthenticator authentication = new(_database.Services.GetRequiredService<IAccountAuthenticationRepository>(),
            hasher, protection, protection, TimeProvider.System);
        AccountAuthenticationResult result = await authentication.AuthenticateAsync(username, "Password123".AsMemory(), IPAddress.Loopback, CancellationToken);
        Assert.Equal(AccountAuthenticationStatus.Success, result.Status);
        Assert.Equal(created.AccountId, result.AccountId);
    }

    [Fact]
    public async Task TryCreateAsync_CancellationDuringFinalInsertRollsBackEarlierRows()
    {
        Guid operationId = Guid.NewGuid();
        await ExecuteAsync("CREATE TRIGGER registration_pause BEFORE INSERT ON account_audit_events FOR EACH ROW DO SLEEP(10);");
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        Task<uint?> create = Store.TryCreateAsync(UniqueName(), PasswordHash, operationId, cancellation.Token).AsTask();
        try
        {
            await using MySqlConnection monitor = new(AdministrativeConnectionString);
            await monitor.OpenAsync(CancellationToken);
            await using MySqlCommand command = monitor.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM performance_schema.processlist WHERE USER = @user AND STATE = 'User sleep'";
            command.Parameters.AddWithValue("@user", RegistrationUserName);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (Convert.ToInt64(await command.ExecuteScalarAsync(deadline.Token), System.Globalization.CultureInfo.InvariantCulture) == 0)
            {
                await Task.Delay(10, deadline.Token);
            }

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => create);
            await AssertOperationAbsentAsync(operationId);
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await create; } catch (OperationCanceledException) { }
            await ExecuteAsync("DROP TRIGGER registration_pause;");
        }
    }

    [Fact]
    public async Task TryCreateAsync_CanceledRequestNeverWrites()
    {
        Guid operationId = Guid.NewGuid();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.TryCreateAsync(UniqueName(), PasswordHash,
            operationId, cancellation.Token).AsTask());
        await AssertOperationAbsentAsync(operationId);
    }

    [Fact]
    public async Task TryCreateAsync_DuplicateOperationIsNotMisreportedAsTakenName()
    {
        Guid operationId = Guid.NewGuid();
        Assert.NotNull(await Store.TryCreateAsync(UniqueName(), PasswordHash, operationId, CancellationToken));
        AccountRegistrationPersistenceException failure = await Assert.ThrowsAsync<AccountRegistrationPersistenceException>(() =>
            Store.TryCreateAsync(UniqueName(), PasswordHash, operationId, CancellationToken).AsTask());
        Assert.Equal(1062, failure.DatabaseErrorNumber);
        Assert.False(failure.CommitOutcomeUnknown);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using MySqlConnection connection = new(AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task AssertOperationAbsentAsync(Guid operationId)
    {
        await using MySqlConnection connection = new(AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM accounts WHERE creation_operation_id = @id";
        command.Parameters.Add("@id", MySqlDbType.Guid).Value = operationId;
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TryCreateAsync_InsertsActivePlayerCredentialAndCreationAudit()
    {
        string username = UniqueName();
        Guid operationId = Guid.NewGuid();

        uint? accountId = await Store.TryCreateAsync(username, PasswordHash, operationId, CancellationToken);

        Assert.NotNull(accountId);

        await using MySqlConnection connection = new(AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.`username`, a.`access_status`, a.`authority_role`, a.`state_revision`, a.`created_by_actor_kind`,
                   a.`state_changed_by_actor_kind`, c.`password_hash`, c.`revision`, c.`password_changed_at_utc` = a.`created_at_utc`,
                   e.`event_kind`, e.`actor_kind`, e.`correlation_id`, e.`new_access_status`, e.`new_authority_role`
            FROM `accounts` a
            JOIN `account_password_credentials` c ON c.`account_id` = a.`account_id`
            JOIN `account_audit_events` e ON e.`account_id` = a.`account_id`
            WHERE a.`account_id` = @account_id AND a.`creation_operation_id` = @operation_id;
            """;
        command.Parameters.AddWithValue("@account_id", accountId!.Value);
        command.Parameters.Add("@operation_id", MySqlDbType.Guid).Value = operationId;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(username, reader.GetString(0));
        Assert.Equal((byte)AccountAccessStatus.Active, reader.GetByte(1));
        Assert.Equal((byte)AccountAuthorityRole.Player, reader.GetByte(2));
        Assert.Equal(1UL, reader.GetUInt64(3));
        Assert.Equal((byte)AccountActorKind.SelfService, reader.GetByte(4));
        Assert.Equal((byte)AccountActorKind.System, reader.GetByte(5));
        Assert.Equal(PasswordHash, reader.GetString(6));
        Assert.Equal(1UL, reader.GetUInt64(7));
        Assert.True(reader.GetBoolean(8));
        Assert.Equal((byte)AccountAuditEventKind.AccountCreated, reader.GetByte(9));
        Assert.Equal((byte)AccountActorKind.SelfService, reader.GetByte(10));
        Assert.Equal(operationId, reader.GetGuid(11));
        Assert.Equal((byte)AccountAccessStatus.Active, reader.GetByte(12));
        Assert.Equal((byte)AccountAuthorityRole.Player, reader.GetByte(13));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task TryCreateAsync_TakenNameIsCaseInsensitiveAndLeavesNoPartialRows()
    {
        string username = UniqueName();

        Assert.NotNull(await Store.TryCreateAsync(username, PasswordHash, Guid.NewGuid(), CancellationToken));

        Guid refusedOperation = Guid.NewGuid();
        Assert.Null(await Store.TryCreateAsync(username.ToUpperInvariant(), PasswordHash, refusedOperation, CancellationToken));

        await using MySqlConnection connection = new(AdministrativeConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `accounts` WHERE `creation_operation_id` = @operation_id;";
        command.Parameters.Add("@operation_id", MySqlDbType.Guid).Value = refusedOperation;

        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TryCreateAsync_ConcurrentSameNameCreatesExactlyOneAccount()
    {
        string username = UniqueName();

        uint?[] results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Store.TryCreateAsync(username, PasswordHash, Guid.NewGuid(), CancellationToken).AsTask()));

        Assert.Single(results, result => result is not null);
    }

    [Fact]
    public async Task TryCreateAsync_RejectsEmptyOperationId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.TryCreateAsync(UniqueName(), PasswordHash, Guid.Empty, CancellationToken).AsTask());
    }

    private static string UniqueName() => "r" + Guid.NewGuid().ToString("N")[..12];
}
