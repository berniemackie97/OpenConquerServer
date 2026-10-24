using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Infrastructure.DependencyInjection;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game;

public sealed class GameDatabaseFixture : IAsyncLifetime
{
    internal const string DatabaseName = "openconquer_game_tests";
    private const string RuntimeUserName = "openconquer_game_runtime_tests";
    private const string SelectOnlyUserName = "openconquer_game_reader_tests"; // MySQL user names stop at 32 characters.
    private static readonly string s_administrativePassword = Guid.NewGuid().ToString("N");

    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4.11").WithDatabase(DatabaseName).WithUsername("root").WithPassword(s_administrativePassword).Build();

    private ServiceProvider? _services;
    private string? _runtimeConnectionString;
    private string? _selectOnlyConnectionString;

    public ServiceProvider Services => _services ?? throw new InvalidOperationException("The game test database is not initialized.");
    public IDbContextFactory<GameDbContext> ContextFactory => Services.GetRequiredService<IDbContextFactory<GameDbContext>>();
    public string AdministrativeConnectionString => _container.GetConnectionString();

    /// <summary>The GameServer runtime identity with exactly the documented Game database grants.</summary>
    public string RuntimeConnectionString => _runtimeConnectionString ?? throw new InvalidOperationException("The game test database is not initialized.");

    /// <summary>
    /// Reads <c>characters</c> but has none of the runtime's write grants, so it shows what the location probe and
    /// stores do when an operator forgets them.
    /// </summary>
    public string SelectOnlyConnectionString => _selectOnlyConnectionString ?? throw new InvalidOperationException("The game test database is not initialized.");
    public ulong InitialCharacterAutoIncrement { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            string administrativeConnectionString = AdministrativeConnectionString;

            await ProvisionSchemaAsync(administrativeConnectionString);

            InitialCharacterAutoIncrement = await ReadCharacterAutoIncrementAsync(administrativeConnectionString);
            _runtimeConnectionString = await CreateIdentityAsync(administrativeConnectionString, RuntimeUserName, RuntimeGrants);
            _selectOnlyConnectionString = await CreateIdentityAsync(administrativeConnectionString, SelectOnlyUserName,
                [$"GRANT SELECT ON `{DatabaseName}`.`characters` TO '{SelectOnlyUserName}'@'%'"]);

            _services = new ServiceCollection().AddGamePersistence(_runtimeConnectionString)
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
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
            _selectOnlyConnectionString = null;
            InitialCharacterAutoIncrement = 0;

            await _container.DisposeAsync();
        }
    }

    private static async Task ProvisionSchemaAsync(string administrativeConnectionString)
    {
        await using ServiceProvider migrationServices = new ServiceCollection().AddGamePersistence(administrativeConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        IDbContextFactory<GameDbContext> factory = migrationServices.GetRequiredService<IDbContextFactory<GameDbContext>>();

        await using GameDbContext db = await factory.CreateDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER DATABASE `openconquer_game_tests`
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """);

        await db.Database.MigrateAsync();
    }

    private static async Task<ulong> ReadCharacterAutoIncrementAsync(string administrativeConnectionString)
    {
        await using MySqlConnection connection = new(administrativeConnectionString);
        await connection.OpenAsync();

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT `AUTO_INCREMENT`
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
            """;

        object? value = await command.ExecuteScalarAsync();

        return value is null or DBNull
            ? throw new InvalidOperationException("The characters table does not expose an AUTO_INCREMENT value after migration.")
            : Convert.ToUInt64(value);
    }

    // docs/operations/database-identities.md: the GameServer runtime grants on its Game database.
    private static string[] RuntimeGrants =>
    [
        $"GRANT SELECT, INSERT ON `{DatabaseName}`.`characters` TO '{RuntimeUserName}'@'%'",
        $"""
            GRANT UPDATE (`map_id`, `position_x`, `position_y`, `location_revision`, `last_location_operation_id`, `location_writer_token`)
            ON `{DatabaseName}`.`characters`
            TO '{RuntimeUserName}'@'%'
            """,
        $"GRANT SELECT ON `{DatabaseName}`.`items` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`social_relations` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`weapon_skills` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`magic` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`syndicates` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`syndicate_memberships` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`schema_compatibility` TO '{RuntimeUserName}'@'%'",
        $"GRANT SELECT ON `{DatabaseName}`.`realm_binding` TO '{RuntimeUserName}'@'%'",
    ];

    private static async Task<string> CreateIdentityAsync(string administrativeConnectionString, string userName, string[] grants)
    {
        string password = Guid.NewGuid().ToString("N");

        await using MySqlConnection connection = new(administrativeConnectionString);
        await connection.OpenAsync();

        await using (MySqlCommand createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"""
                CREATE USER '{userName}'@'%'
                IDENTIFIED BY '{password}'
                """;

            await createUser.ExecuteNonQueryAsync();
        }

        foreach (string grant in grants)
        {
            await using MySqlCommand command = connection.CreateCommand();
            command.CommandText = grant;
            await command.ExecuteNonQueryAsync();
        }

        MySqlConnectionStringBuilder identity = new(administrativeConnectionString) { UserID = userName, Password = password };
        return identity.ConnectionString;
    }
}
