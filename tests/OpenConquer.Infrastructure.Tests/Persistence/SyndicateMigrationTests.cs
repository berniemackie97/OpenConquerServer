using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class SyndicateMigrationTests
{
    private const string DatabaseName = "openconquer_game_syndicate_migration_tests";
    private const string PreviousMigrationId = "20261004193352_AddMagicPersistence";
    private const string CurrentMigrationId = "20261008033456_AddSyndicatePersistence";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddSyndicatePersistence_MigratesV8AndPreservesSchemaContract()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, PreviousMigrationId);
        await AssertCompatibilityAsync(connectionString, 8, PreviousMigrationId);
        await AssertTableCountAsync(connectionString, 0);

        await MigrateAsync(connectionString, CurrentMigrationId);

        await AssertCompatibilityAsync(connectionString, 9, CurrentMigrationId);
        await AssertTableCountAsync(connectionString, 2);
        await AssertStructureAsync(connectionString);
        await AssertMigrationHistoryAsync(connectionString, CurrentMigrationId, present: true);
    }

    [Fact]
    public async Task AddSyndicatePersistence_WhenFirstTableExistsWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await ExecuteSqlAsync(connectionString, "DROP TABLE `syndicate_memberships`");
        await UndoMigrationMetadataAsync(connectionString);
        await AssertTableCountAsync(connectionString, 1);

        await MigrateAsync(connectionString, CurrentMigrationId);

        await AssertStructureAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 9, CurrentMigrationId);
        await AssertMigrationHistoryAsync(connectionString, CurrentMigrationId, present: true);
    }

    [Fact]
    public async Task AddSyndicatePersistence_WhenBothTablesAndCompatibilityExistWithoutHistory_ResumesSafely()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await ExecuteSqlAsync(connectionString, $"DELETE FROM `__EFMigrationsHistory` WHERE `MigrationId` = '{CurrentMigrationId}'");

        await MigrateAsync(connectionString, CurrentMigrationId);

        await AssertStructureAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 9, CurrentMigrationId);
        await AssertMigrationHistoryAsync(connectionString, CurrentMigrationId, present: true);
    }

    [Fact]
    public async Task AddSyndicatePersistence_UnexpectedIndex_FailsClosedWithoutAdvancingCompatibility()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await UndoMigrationMetadataAsync(connectionString);
        await ExecuteSqlAsync(connectionString, "CREATE INDEX `IX_syndicates_test` ON `syndicates` (`emoney_fund`)");

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => MigrateAsync(connectionString, CurrentMigrationId));

        Assert.Equal(3819, exception.Number);
        await AssertCompatibilityAsync(connectionString, 8, PreviousMigrationId);
        await AssertMigrationHistoryAsync(connectionString, CurrentMigrationId, present: false);
    }

    [Fact]
    public async Task AddSyndicatePersistence_UnexpectedMembershipCheck_FailsClosed()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await UndoMigrationMetadataAsync(connectionString);
        await ExecuteSqlAsync(connectionString,
            "ALTER TABLE `syndicate_memberships` ADD CONSTRAINT `CK_syndicate_memberships_rank` CHECK (`rank` > 0)");

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => MigrateAsync(connectionString, CurrentMigrationId));

        Assert.Equal(3819, exception.Number);
        await AssertCompatibilityAsync(connectionString, 8, PreviousMigrationId);
    }

    [Fact]
    public async Task AddSyndicatePersistence_DownOnEmptyTablesRestoresV8()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await MigrateAsync(connectionString, PreviousMigrationId);

        await AssertTableCountAsync(connectionString, 0);
        await AssertCompatibilityAsync(connectionString, 8, PreviousMigrationId);
        await AssertMigrationHistoryAsync(connectionString, CurrentMigrationId, present: false);
    }

    [Fact]
    public async Task AddSyndicatePersistence_DownWithPersistedSyndicateFailsWithoutDataLoss()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        uint leaderCharacterId = await InsertCharacterAsync(connectionString);
        await InsertSyndicateAsync(connectionString, leaderCharacterId);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => MigrateAsync(connectionString, PreviousMigrationId));

        Assert.Equal(3819, exception.Number);
        await AssertTableCountAsync(connectionString, 2);
        await AssertCompatibilityAsync(connectionString, 9, CurrentMigrationId);
        Assert.Equal(1L, await ScalarInt64Async(connectionString, "SELECT COUNT(*) FROM `syndicates`"));
    }

    [Fact]
    public async Task AddSyndicatePersistence_DownAfterInterruptedTableDropResumesSafely()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        await ExecuteSqlAsync(connectionString, "DROP TABLE `syndicate_memberships`");

        await MigrateAsync(connectionString, PreviousMigrationId);

        await AssertTableCountAsync(connectionString, 0);
        await AssertCompatibilityAsync(connectionString, 8, PreviousMigrationId);
    }

    [Fact]
    public async Task AddSyndicatePersistence_EnforcesSyndicateNameLength()
    {
        await using MySqlContainer database = CreateContainer();
        await database.StartAsync(CancellationToken);
        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, CurrentMigrationId);
        uint leaderCharacterId = await InsertCharacterAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() => ExecuteSqlAsync(connectionString,
            $"INSERT INTO `syndicates` (`name`, `leader_character_id`, `silver_fund`, `emoney_fund`, `required_level`, `required_profession`, `required_metempsychosis`) VALUES ('', {leaderCharacterId}, 0, 0, 0, 0, 0)"));

        Assert.Equal(3819, exception.Number);
        Assert.Equal(0L, await ScalarInt64Async(connectionString, "SELECT COUNT(*) FROM `syndicates`"));
    }

    private static MySqlContainer CreateContainer()
    {
        return new MySqlBuilder("mysql:8.4.11").WithDatabase(DatabaseName).WithUsername("root")
            .WithPassword(Guid.NewGuid().ToString("N")).Build();
    }

    private static GameDbContext CreateDbContext(string connectionString)
    {
        DbContextOptionsBuilder<GameDbContext> options = new();
        GameDbContextOptionsConfiguration.Configure(options, connectionString);
        return new GameDbContext(options.Options);
    }

    private static async Task ConfigureDatabaseAsync(string connectionString)
    {
        await using GameDbContext db = CreateDbContext(connectionString);
        await db.Database.ExecuteSqlRawAsync($"ALTER DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs", CancellationToken);
    }

    private static async Task MigrateAsync(string connectionString, string migrationId)
    {
        await using GameDbContext db = CreateDbContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migrationId, CancellationToken);
    }

    private static async Task UndoMigrationMetadataAsync(string connectionString)
    {
        await ExecuteSqlAsync(connectionString,
            $"DELETE FROM `__EFMigrationsHistory` WHERE `MigrationId` = '{CurrentMigrationId}'; " +
            $"UPDATE `schema_compatibility` SET `schema_version` = 8, `migration_id` = '{PreviousMigrationId}', " +
            "`applied_at_utc` = UTC_TIMESTAMP(6) WHERE `component_name` = 'game'");
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task<long> ScalarInt64Async(string connectionString, string sql)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken));
    }

    private static async Task<uint> InsertCharacterAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters` (`account_id`, `name`, `appearance_composite`, `hair_composite`, `level`, `experience`,
                `strength`, `agility`, `vitality`, `spirit`, `unspent_attribute_points`, `current_life`, `current_mana`,
                `profession`, `first_profession`, `previous_profession`, `rebirth_count`, `pre_rebirth_level`, `silver`,
                `conquer_points`, `bound_conquer_points`, `pk_points`, `title_id`, `enlightenment_points`, `map_id`,
                `position_x`, `position_y`)
            VALUES (900001, 'SynLeader', 1003, 410, 1, 0, 10, 10, 10, 10, 0, 100, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1002, 430, 378)
            """;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
        return checked((uint)command.LastInsertedId);
    }

    private static async Task InsertSyndicateAsync(string connectionString, uint leaderCharacterId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `syndicates` (`name`, `leader_character_id`, `silver_fund`, `emoney_fund`,
                `required_level`, `required_profession`, `required_metempsychosis`)
            VALUES ('OpenConquer', @leader_id, 0, 0, 1, 0, 0)
            """;
        command.Parameters.Add("@leader_id", MySqlDbType.UInt32).Value = leaderCharacterId;
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task AssertCompatibilityAsync(string connectionString, uint version, string migrationId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT `schema_version`, `migration_id` FROM `schema_compatibility` WHERE `component_name` = 'game'";
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(version, reader.GetUInt32(0));
        Assert.Equal(migrationId, reader.GetString(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertTableCountAsync(string connectionString, long expected)
    {
        long actual = await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLES` WHERE `TABLE_SCHEMA` = DATABASE() " +
            "AND `TABLE_NAME` IN ('syndicates', 'syndicate_memberships') AND `TABLE_TYPE` = 'BASE TABLE'");
        Assert.Equal(expected, actual);
    }

    private static async Task AssertStructureAsync(string connectionString)
    {
        Assert.Equal(8L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`COLUMNS` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates'"));
        Assert.Equal(6L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`COLUMNS` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships'"));
        Assert.Equal(3L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`STATISTICS` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates'"));
        Assert.Equal(3L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`STATISTICS` WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships'"));
        Assert.Equal(2L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` WHERE `CONSTRAINT_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates' AND `CONSTRAINT_TYPE` = 'CHECK' AND `ENFORCED` = 'YES'"));
        Assert.Equal(2L, await ScalarInt64Async(connectionString,
            "SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` WHERE `CONSTRAINT_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships' AND `CONSTRAINT_TYPE` = 'CHECK' AND `ENFORCED` = 'YES'"));
    }

    private static async Task AssertMigrationHistoryAsync(string connectionString, string migrationId, bool present)
    {
        long actual = await ScalarInt64Async(connectionString,
            $"SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE `MigrationId` = '{migrationId}'");
        Assert.Equal(present ? 1L : 0L, actual);
    }
}
