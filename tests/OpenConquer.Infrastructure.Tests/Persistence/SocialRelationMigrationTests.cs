using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using OpenConquer.Domain.Social;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class SocialRelationMigrationTests
{
    private const string DatabaseName = "openconquer_game_social_migration_tests";
    private const string InitialMigrationId = "20260914210346_InitialGameSchema";
    private const string SignedPkPointsMigrationId = "20260922025854_UseSignedCharacterPkPoints";
    private const string PreRebirthLevelMigrationId = "20260923223920_RetainPreRebirthLevel";
    private const string ItemPersistenceMigrationId = "20260927121811_AddItemPersistenceFoundation";
    private const string ItemLifetimePersistenceMigrationId = "20260927225127_AddItemLifetimePersistence";
    private const string SocialRelationPersistenceMigrationId = "20260930033806_AddSocialRelationPersistence";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddSocialRelationPersistence_MigratesV5SchemaAndAdvancesContract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertSocialRelationsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);

        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_WhenStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);
        await CreateSocialRelationsTableAsync(connectionString);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);

        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_WhenStructureAndCompatibilityCommittedWithoutHistory_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);
        await CreateSocialRelationsTableAsync(connectionString);
        await UpdateCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);

        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_WhenExistingTableIsIncompatible_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);
        await CreateIncompatibleSocialRelationsTableAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, SocialRelationPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertSocialRelationColumnCountAsync(connectionString, expectedCount: 1);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_DownMigrationRemovesEmptyTableAndRestoresV5Contract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);

        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertSocialRelationsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_DownMigrationWithPersistedRelations_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        uint ownerCharacterId = await InsertCharacterAsync(connectionString, accountId: 500_001, "SocialOwner");
        uint counterpartCharacterId = await InsertCharacterAsync(connectionString, accountId: 500_002, "SocialPeer");

        await InsertRelationAsync(connectionString, ownerCharacterId, counterpartCharacterId, SocialRelationKind.Friend);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertSocialRelationsStructurePresentAsync(connectionString);
        await AssertSocialRelationRowCountAsync(connectionString, expectedCount: 1);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddSocialRelationPersistence_DownMigrationWhenTableAlreadyDropped_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);
        await DropSocialRelationsTableAsync(connectionString);

        await AssertSocialRelationsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 6, SocialRelationPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId);

        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertSocialRelationsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);
    }

    private static MySqlContainer CreateDatabaseContainer()
    {
        string administrativePassword = Guid.NewGuid().ToString("N");

        return new MySqlBuilder("mysql:8.4.11")
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(administrativePassword)
            .Build();
    }

    private static async Task ConfigureDatabaseAsync(string connectionString)
    {
        await using GameDbContext db = CreateDbContext(connectionString);

        await db.Database.ExecuteSqlRawAsync(
            $"""
            ALTER DATABASE `{DatabaseName}`
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """,
            CancellationToken);
    }

    private static async Task MigrateAsync(string connectionString, string migrationId)
    {
        await using GameDbContext db = CreateDbContext(connectionString);
        IMigrator migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(migrationId, CancellationToken);
    }

    private static GameDbContext CreateDbContext(string connectionString)
    {
        DbContextOptionsBuilder<GameDbContext> options = new();
        GameDbContextOptionsConfiguration.Configure(options, connectionString);

        return new GameDbContext(options.Options);
    }

    private static async Task CreateSocialRelationsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE `social_relations`
            (
                `owner_character_id` INT UNSIGNED NOT NULL,
                `counterpart_character_id` INT UNSIGNED NOT NULL,
                `kind` TINYINT UNSIGNED NOT NULL,

                CONSTRAINT `PK_social_relations`
                    PRIMARY KEY (`owner_character_id`, `kind`, `counterpart_character_id`),

                KEY `IX_social_relations_counterpart_kind_owner`
                    (`counterpart_character_id`, `kind`, `owner_character_id`),

                CONSTRAINT `FK_social_relations_characters_owner_character_id`
                    FOREIGN KEY (`owner_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `FK_social_relations_characters_counterpart_character_id`
                    FOREIGN KEY (`counterpart_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `CK_social_relations_owner_character_id`
                    CHECK (`owner_character_id` >= 1000000),

                CONSTRAINT `CK_social_relations_counterpart_character_id`
                    CHECK (`counterpart_character_id` >= 1000000),

                CONSTRAINT `CK_social_relations_distinct_characters`
                    CHECK (`owner_character_id` <> `counterpart_character_id`),

                CONSTRAINT `CK_social_relations_kind`
                    CHECK (`kind` IN (1, 2))
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task CreateIncompatibleSocialRelationsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE `social_relations`
            (
                `owner_character_id` INT UNSIGNED NOT NULL
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task DropSocialRelationsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "DROP TABLE `social_relations`";

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task UpdateCompatibilityAsync(string connectionString, uint expectedSchemaVersion, string migrationId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE `schema_compatibility`
            SET
                `schema_version` = @schema_version,
                `migration_id` = @migration_id,
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
            """;
        command.Parameters.Add("@schema_version", MySqlDbType.UInt32).Value = expectedSchemaVersion;
        command.Parameters.Add("@migration_id", MySqlDbType.VarChar).Value = migrationId;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task<uint> InsertCharacterAsync(string connectionString, uint accountId, string name)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters`
                (`account_id`, `name`, `appearance_composite`, `hair_composite`, `level`, `experience`, `strength`, `agility`, `vitality`, `spirit`,
                 `unspent_attribute_points`, `current_life`, `current_mana`, `profession`, `first_profession`, `previous_profession`, `rebirth_count`,
                 `pre_rebirth_level`, `silver`, `conquer_points`, `bound_conquer_points`, `pk_points`, `title_id`, `enlightenment_points`, `map_id`,
                 `position_x`, `position_y`)
            VALUES
                (@account_id, @name, 1003, 410, 1, 0, 10, 10, 10, 10, 0, 100, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1002, 430, 378)
            """;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return checked((uint)command.LastInsertedId);
    }

    private static async Task InsertRelationAsync(string connectionString, uint ownerCharacterId, uint counterpartCharacterId, SocialRelationKind kind)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `social_relations`
                (`owner_character_id`, `counterpart_character_id`, `kind`)
            VALUES
                (@owner_character_id, @counterpart_character_id, @kind)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@counterpart_character_id", MySqlDbType.UInt32).Value = counterpartCharacterId;
        command.Parameters.Add("@kind", MySqlDbType.UByte).Value = (byte)kind;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task AssertSocialRelationsStructurePresentAsync(string connectionString)
    {
        await AssertSocialRelationColumnCountAsync(connectionString, expectedCount: 3);

        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`STATISTICS`
                 WHERE `TABLE_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'social_relations'
                   AND `INDEX_NAME` IN ('PRIMARY', 'IX_social_relations_counterpart_kind_owner')),

                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE`
                 WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'social_relations'
                   AND `REFERENCED_TABLE_NAME` = 'characters'),

                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                 WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'social_relations'
                   AND `CONSTRAINT_TYPE` = 'CHECK'
                   AND `ENFORCED` = 'YES'),

                (SELECT `TABLE_COLLATION`
                 FROM `INFORMATION_SCHEMA`.`TABLES`
                 WHERE `TABLE_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'social_relations'
                   AND `TABLE_TYPE` = 'BASE TABLE')
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(6L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(4L, reader.GetInt64(2));
        Assert.Equal("utf8mb4_0900_as_cs", reader.GetString(3));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertSocialRelationsTableAbsentAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0L, Convert.ToInt64(result));
    }

    private static async Task AssertSocialRelationColumnCountAsync(string connectionString, int expectedCount)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'social_relations'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, Convert.ToInt32(result));
    }

    private static async Task AssertSocialRelationRowCountAsync(string connectionString, long expectedCount)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `social_relations`";

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, Convert.ToInt64(result));
    }

    private static async Task AssertCompatibilityAsync(string connectionString, uint expectedSchemaVersion, string expectedMigrationId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `schema_version`, `migration_id`
            FROM `schema_compatibility`
            WHERE `component_name` = 'game'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(expectedSchemaVersion, reader.GetUInt32(0));
        Assert.Equal(expectedMigrationId, reader.GetString(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertMigrationHistoryAsync(string connectionString, params string[] expected)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `MigrationId`
            FROM `__EFMigrationsHistory`
            ORDER BY `MigrationId`
            """;

        List<string> actual = [];

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(expected, actual);
    }
}
