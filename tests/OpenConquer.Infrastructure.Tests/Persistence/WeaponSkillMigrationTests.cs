using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class WeaponSkillMigrationTests
{
    private const string DatabaseName = "openconquer_game_weapon_skill_migration_tests";
    private const string InitialMigrationId = "20260914210346_InitialGameSchema";
    private const string SignedPkPointsMigrationId = "20260922025854_UseSignedCharacterPkPoints";
    private const string PreRebirthLevelMigrationId = "20260923223920_RetainPreRebirthLevel";
    private const string ItemPersistenceMigrationId = "20260927121811_AddItemPersistenceFoundation";
    private const string ItemLifetimePersistenceMigrationId = "20260927225127_AddItemLifetimePersistence";
    private const string SocialRelationPersistenceMigrationId = "20260930033806_AddSocialRelationPersistence";
    private const string WeaponSkillPersistenceMigrationId = "20261004014402_AddWeaponSkillPersistence";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> UnexpectedCanonicalStructureCases =>
        new()
        {
            {
                "extra index",
                """
                CREATE INDEX `IX_weapon_skills_level`
                ON `weapon_skills` (`level`)
                """
            },
            {
                "extra unsupported check",
                """
                ALTER TABLE `weapon_skills`
                ADD CONSTRAINT `CK_weapon_skills_type_positive`
                    CHECK (`weapon_skill_type` > 0)
                """
            },
        };

    [Fact]
    public async Task AddWeaponSkillPersistence_MigratesV6SchemaAndAdvancesContract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertWeaponSkillTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);

        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);

        await AssertWeaponSkillStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 7, WeaponSkillPersistenceMigrationId);
        await AssertMigrationHistoryAsync(connectionString,
            InitialMigrationId, SignedPkPointsMigrationId, PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId, ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId, WeaponSkillPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_WhenStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);
        await CreateWeaponSkillsTableAsync(connectionString);

        await AssertWeaponSkillStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);

        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);

        await AssertWeaponSkillStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 7, WeaponSkillPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_WhenStructureAndCompatibilityCommittedWithoutHistory_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);
        await CreateWeaponSkillsTableAsync(connectionString);
        await UpdateCompatibilityAsync(connectionString, 7, WeaponSkillPersistenceMigrationId);

        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);

        await AssertWeaponSkillStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 7, WeaponSkillPersistenceMigrationId);
        await AssertMigrationHistoryAsync(connectionString,
            InitialMigrationId, SignedPkPointsMigrationId, PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId, ItemLifetimePersistenceMigrationId,
            SocialRelationPersistenceMigrationId, WeaponSkillPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_WhenExistingTableIsIncompatible_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);
        await CreateIncompatibleWeaponSkillsTableAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);
        await AssertWeaponSkillColumnCountAsync(connectionString, 1);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);
    }

    [Theory]
    [MemberData(nameof(UnexpectedCanonicalStructureCases))]
    public async Task AddWeaponSkillPersistence_WhenCanonicalTableContainsUnexpectedStructure_FailsClosed(string _, string structureSql)
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);
        await CreateWeaponSkillsTableAsync(connectionString);
        await ExecuteSqlAsync(connectionString, structureSql);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_DownMigrationRemovesEmptyTableAndRestoresV6Contract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);

        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertWeaponSkillTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_DownMigrationWithPersistedSkills_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);

        uint characterId = await InsertCharacterAsync(connectionString, 700_001, "SkillOwner");
        await InsertSkillAsync(connectionString, characterId, 410);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, SocialRelationPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertWeaponSkillStructurePresentAsync(connectionString);
        await AssertWeaponSkillRowCountAsync(connectionString, 1);
        await AssertCompatibilityAsync(connectionString, 7, WeaponSkillPersistenceMigrationId);
    }

    [Fact]
    public async Task AddWeaponSkillPersistence_DownMigrationWhenTableAlreadyDropped_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, WeaponSkillPersistenceMigrationId);
        await DropWeaponSkillsTableAsync(connectionString);

        await MigrateAsync(connectionString, SocialRelationPersistenceMigrationId);

        await AssertWeaponSkillTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, 6, SocialRelationPersistenceMigrationId);
    }

    private static MySqlContainer CreateDatabaseContainer()
    {
        return new MySqlBuilder("mysql:8.4.11")
            .WithDatabase(DatabaseName)
            .WithUsername("root")
            .WithPassword(Guid.NewGuid().ToString("N"))
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

    private static async Task CreateWeaponSkillsTableAsync(string connectionString)
    {
        await ExecuteSqlAsync(connectionString,
            """
            CREATE TABLE `weapon_skills`
            (
                `owner_character_id` INT UNSIGNED NOT NULL,
                `weapon_skill_type` INT UNSIGNED NOT NULL,
                `level` TINYINT UNSIGNED NOT NULL,
                `experience` INT UNSIGNED NOT NULL,

                CONSTRAINT `PK_weapon_skills`
                    PRIMARY KEY (`owner_character_id`, `weapon_skill_type`),

                CONSTRAINT `FK_weapon_skills_characters_owner_character_id`
                    FOREIGN KEY (`owner_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `CK_weapon_skills_owner_character_id`
                    CHECK (`owner_character_id` >= 1000000),

                CONSTRAINT `CK_weapon_skills_level`
                    CHECK (`level` <= 20)
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """);
    }

    private static async Task CreateIncompatibleWeaponSkillsTableAsync(string connectionString)
    {
        await ExecuteSqlAsync(connectionString,
            """
            CREATE TABLE `weapon_skills`
            (
                `owner_character_id` INT UNSIGNED NOT NULL
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """);
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task DropWeaponSkillsTableAsync(string connectionString)
    {
        await ExecuteSqlAsync(connectionString, "DROP TABLE `weapon_skills`");
    }

    private static async Task UpdateCompatibilityAsync(string connectionString, uint schemaVersion, string migrationId)
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
        command.Parameters.Add("@schema_version", MySqlDbType.UInt32).Value = schemaVersion;
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

    private static async Task InsertSkillAsync(string connectionString, uint characterId, uint type)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `weapon_skills`
                (`owner_character_id`, `weapon_skill_type`, `level`, `experience`)
            VALUES
                (@owner_character_id, @weapon_skill_type, 1, 0)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = characterId;
        command.Parameters.Add("@weapon_skill_type", MySqlDbType.UInt32).Value = type;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task AssertWeaponSkillStructurePresentAsync(string connectionString)
    {
        await AssertWeaponSkillColumnCountAsync(connectionString, 4);

        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`STATISTICS`
                 WHERE `TABLE_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'weapon_skills'),

                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE`
                 WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'weapon_skills'
                   AND `REFERENCED_TABLE_NAME` = 'characters'),

                (SELECT COUNT(*)
                 FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                 WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'weapon_skills'
                   AND `CONSTRAINT_TYPE` = 'CHECK'
                   AND `ENFORCED` = 'YES'),

                (SELECT `TABLE_COLLATION`
                 FROM `INFORMATION_SCHEMA`.`TABLES`
                 WHERE `TABLE_SCHEMA` = DATABASE()
                   AND `TABLE_NAME` = 'weapon_skills'
                   AND `TABLE_TYPE` = 'BASE TABLE')
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal("utf8mb4_0900_as_cs", reader.GetString(3));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertWeaponSkillTableAbsentAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'weapon_skills'
            """;

        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken)));
    }

    private static async Task AssertWeaponSkillColumnCountAsync(string connectionString, int expected)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'weapon_skills'
            """;

        Assert.Equal(expected, Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken)));
    }

    private static async Task AssertWeaponSkillRowCountAsync(string connectionString, long expected)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM `weapon_skills`";

        Assert.Equal(expected, Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken)));
    }

    private static async Task AssertCompatibilityAsync(string connectionString, uint schemaVersion, string migrationId)
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
        Assert.Equal(schemaVersion, reader.GetUInt32(0));
        Assert.Equal(migrationId, reader.GetString(1));
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
