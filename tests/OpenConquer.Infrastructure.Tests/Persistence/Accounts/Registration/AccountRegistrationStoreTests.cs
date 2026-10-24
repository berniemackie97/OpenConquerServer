using MySqlConnector;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Registration;

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

    public AccountRegistrationStoreTests(AccountDatabaseFixture database)
    {
        _database = database;
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
    private AccountRegistrationStore Store => _store ?? throw new InvalidOperationException("The store is not initialized.");

    public async ValueTask InitializeAsync()
    {
        string password = Guid.NewGuid().ToString("N");

        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);
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

        MySqlConnectionStringBuilder registration = new(_database.AdministrativeConnectionString)
        {
            UserID = RegistrationUserName,
            Password = password,
        };

        _store = new AccountRegistrationStore(registration.ConnectionString);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TryCreateAsync_InsertsActivePlayerCredentialAndCreationAudit()
    {
        string username = UniqueName();
        Guid operationId = Guid.NewGuid();

        uint? accountId = await Store.TryCreateAsync(username, PasswordHash, operationId, CancellationToken);

        Assert.NotNull(accountId);

        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);
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

        await using MySqlConnection connection = new(_database.AdministrativeConnectionString);
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
