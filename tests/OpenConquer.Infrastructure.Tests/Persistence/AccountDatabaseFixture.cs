using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class AccountDatabaseFixture : IAsyncLifetime
{
    internal const string DatabaseName = "openconquer_accounts_tests";
    private const string RuntimeUserName = "openconquer_runtime_tests";
    private static readonly string s_administrativePassword = Guid.NewGuid().ToString("N");

    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4.11").WithDatabase(DatabaseName).WithUsername("root").WithPassword(s_administrativePassword).Build();

    private ServiceProvider? _services;
    private string? _runtimeConnectionString;

    public ServiceProvider Services => _services ?? throw new InvalidOperationException("The account test database is not initialized.");
    public IDbContextFactory<AccountDbContext> ContextFactory => Services.GetRequiredService<IDbContextFactory<AccountDbContext>>();

    public string AdministrativeConnectionString => _container.GetConnectionString();
    public string RuntimeConnectionString => _runtimeConnectionString ?? throw new InvalidOperationException("The account test database is not initialized.");

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            string administrativeConnectionString = AdministrativeConnectionString;

            await ProvisionSchemaAsync(administrativeConnectionString);

            _runtimeConnectionString = await CreateRuntimeIdentityAsync(administrativeConnectionString);

            _services = new ServiceCollection().AddAccountPersistence(_runtimeConnectionString).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }
        catch
        {
            await DisposeAsync();

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_services is not null)
            {
                await _services.DisposeAsync();
            }
        }
        finally
        {
            _services = null;
            _runtimeConnectionString = null;

            await _container.DisposeAsync();
        }
    }

    private static async Task ProvisionSchemaAsync(string administrativeConnectionString)
    {
        await using ServiceProvider migrationServices = new ServiceCollection().AddAccountPersistence(administrativeConnectionString).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        IDbContextFactory<AccountDbContext> factory = migrationServices.GetRequiredService<IDbContextFactory<AccountDbContext>>();

        await using AccountDbContext db = await factory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER DATABASE `openconquer_accounts_tests`
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """);

        await db.Database.MigrateAsync();
    }

    private static async Task<string> CreateRuntimeIdentityAsync(string administrativeConnectionString)
    {
        string password = Guid.NewGuid().ToString("N");

        await using MySqlConnection connection = new(administrativeConnectionString);

        await connection.OpenAsync();

        await using (MySqlCommand createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"""
                CREATE USER '{RuntimeUserName}'@'%'
                IDENTIFIED BY '{password}'
                """;

            await createUser.ExecuteNonQueryAsync();
        }

        string[] grants =
        [
            $"""
                GRANT SELECT, INSERT, UPDATE
                ON `{DatabaseName}`.`accounts`
                TO '{RuntimeUserName}'@'%'
                """,
            $"""
                GRANT SELECT, INSERT, UPDATE
                ON `{DatabaseName}`.`account_password_credentials`
                TO '{RuntimeUserName}'@'%'
                """,
            $"""
                GRANT SELECT, INSERT
                ON `{DatabaseName}`.`account_audit_events`
                TO '{RuntimeUserName}'@'%'
                """,
            $"""
                GRANT SELECT, INSERT, DELETE
                ON `{DatabaseName}`.`game_login_tickets`
                TO '{RuntimeUserName}'@'%'
                """,
            $"""
                GRANT SELECT
                ON `{DatabaseName}`.`schema_compatibility`
                TO '{RuntimeUserName}'@'%'
                """,
        ];

        foreach (string grant in grants)
        {
            await using MySqlCommand command = connection.CreateCommand();

            command.CommandText = grant;

            await command.ExecuteNonQueryAsync();
        }

        MySqlConnectionStringBuilder runtimeConnection = new(administrativeConnectionString)
        {
            UserID = RuntimeUserName,
            Password = password,
        };

        return runtimeConnection.ConnectionString;
    }
}
