using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using OpenConquer.Domain.Items;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class ItemMigrationTests
{
    private const string DatabaseName = "openconquer_game_item_migration_tests";
    private const string InitialMigrationId = "20260914210346_InitialGameSchema";
    private const string SignedPkPointsMigrationId = "20260922025854_UseSignedCharacterPkPoints";
    private const string PreRebirthLevelMigrationId = "20260923223920_RetainPreRebirthLevel";
    private const string ItemPersistenceMigrationId = "20260927121811_AddItemPersistenceFoundation";
    private const string ItemLifetimePersistenceMigrationId = "20260927225127_AddItemLifetimePersistence";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddItemPersistenceFoundation_MigratesV3SchemaAndAdvancesContract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);

        await AssertItemsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);

        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemPersistenceFoundation_WhenStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);
        await CreateItemsTableAsync(connectionString);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);

        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemPersistenceFoundation_WhenStructureAndCompatibilityCommittedWithoutHistory_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);
        await CreateItemsTableAsync(connectionString);
        await UpdateCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);

        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemPersistenceFoundation_WhenExistingItemsTableIsIncompatible_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);
        await CreateIncompatibleItemsTableAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertItemColumnCountAsync(connectionString, expectedCount: 1);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);
    }

    [Fact]
    public async Task AddItemPersistenceFoundation_DownMigrationRemovesItemsAndRestoresV3Contract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemsTablePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);

        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);

        await AssertItemsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);
    }

    [Fact]
    public async Task AddItemPersistenceFoundation_DownMigrationWhenTableAlreadyDropped_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await DropItemsTableAsync(connectionString);

        await AssertItemsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);

        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);

        await AssertItemsTableAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_MigratesV4SchemaAndAdvancesContract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 26);
        await AssertLifetimePersistenceStructureAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);

        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
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
    public async Task AddItemLifetimePersistence_WhenPartialStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await AddLifetimeStateColumnAsync(connectionString);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 27);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);

        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
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
    public async Task AddItemLifetimePersistence_WhenStructureAndCompatibilityCommittedWithoutHistory_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await CreateLifetimePersistenceStructureAsync(connectionString);
        await UpdateCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);

        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
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
    public async Task AddItemLifetimePersistence_WhenExistingLifetimeColumnIsIncompatible_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await AddIncompatibleLifetimeStateColumnAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 27);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_WhenLifetimeColumnHasWrongOrdinal_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await AddMisorderedLifetimeStateColumnAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 27);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_WhenExistingLifetimeCheckIsIncompatible_FailsClosed()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);
        await CreateIncompatibleLifetimePersistenceStructureAsync(connectionString);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_WhenV4ItemsExist_FailsClosedWithoutInventingLifetimeState()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        uint characterId = await InsertCharacterAsync(connectionString);
        await InsertV4ItemAsync(connectionString, characterId);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 26);
        await AssertLifetimePersistenceStructureAbsentAsync(connectionString);
        await AssertItemRowCountAsync(connectionString, expectedCount: 1);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_DownMigrationRemovesLifetimePersistenceAndRestoresV4Contract()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);

        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 26);
        await AssertLifetimePersistenceStructureAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_DownMigrationWhenStructureAlreadyRemoved_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);
        await DropLifetimePersistenceStructureAsync(connectionString);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 26);
        await AssertLifetimePersistenceStructureAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 5, ItemLifetimePersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId,
            ItemLifetimePersistenceMigrationId);

        await MigrateAsync(connectionString, ItemPersistenceMigrationId);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 26);
        await AssertLifetimePersistenceStructureAbsentAsync(connectionString);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 4, ItemPersistenceMigrationId);
        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            SignedPkPointsMigrationId,
            PreRebirthLevelMigrationId,
            ItemPersistenceMigrationId);
    }

    [Fact]
    public async Task AddItemLifetimePersistence_DownMigrationWithItems_FailsClosedWithoutDiscardingLifetimeState()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, ItemLifetimePersistenceMigrationId);

        uint characterId = await InsertCharacterAsync(connectionString);
        await InsertV5ItemAsync(connectionString, characterId);

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, ItemPersistenceMigrationId));

        Assert.Equal(3819, exception.Number);

        await AssertItemColumnCountAsync(connectionString, expectedCount: 29);
        await AssertLifetimePersistenceStructurePresentAsync(connectionString);
        await AssertItemRowCountAsync(connectionString, expectedCount: 1);
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

    private static async Task CreateItemsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE `items`
            (
                `item_id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
                `owner_character_id` INT UNSIGNED NOT NULL,
                `item_type_id` INT UNSIGNED NOT NULL,
                `location_kind` TINYINT UNSIGNED NOT NULL,
                `equipment_set` TINYINT UNSIGNED NULL,
                `equipment_slot` TINYINT UNSIGNED NULL,
                `durability` SMALLINT UNSIGNED NOT NULL,
                `maximum_durability` SMALLINT UNSIGNED NOT NULL,
                `retail_compatibility_byte_a` TINYINT UNSIGNED NOT NULL,
                `socket_progress_or_steed_color_or_monster_counter_baseline` INT UNSIGNED NOT NULL,
                `socket1_code` TINYINT UNSIGNED NOT NULL,
                `socket2_code` TINYINT UNSIGNED NOT NULL,
                `hidden_attack_effect` INT UNSIGNED NOT NULL,
                `retail_compatibility_byte_b` TINYINT UNSIGNED NOT NULL,
                `addition_level` TINYINT UNSIGNED NOT NULL,
                `damage_reduction_percent_or_steed_composition_red` TINYINT UNSIGNED NOT NULL,
                `item_binding_code` TINYINT UNSIGNED NOT NULL,
                `enchantment_life_bonus_or_steed_composition_green` TINYINT UNSIGNED NOT NULL,
                `monster_restraint_id_or_steed_composition_blue` INT UNSIGNED NOT NULL,
                `is_suspicious` TINYINT(1) NOT NULL,
                `equipment_lock_state_mask` SMALLINT UNSIGNED NOT NULL,
                `equipment_unlock_at_utc` DATETIME(6) NULL,
                `equipment_color` SMALLINT UNSIGNED NOT NULL,
                `composition_progress` INT UNSIGNED NOT NULL,
                `inscribed_syndicate_id` INT UNSIGNED NOT NULL,
                `stack_quantity` SMALLINT UNSIGNED NOT NULL,

                CONSTRAINT `PK_items`
                    PRIMARY KEY (`item_id`),

                CONSTRAINT `FK_items_characters_owner_character_id`
                    FOREIGN KEY (`owner_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `CK_items_item_type_id`
                    CHECK (`item_type_id` > 0),

                CONSTRAINT `CK_items_location_kind`
                    CHECK (`location_kind` IN (1, 2)),

                CONSTRAINT `CK_items_location_payload`
                    CHECK (
                        (`location_kind` = 1 AND `equipment_set` IS NULL AND `equipment_slot` IS NULL)
                        OR
                        (`location_kind` = 2 AND `equipment_set` IS NOT NULL AND `equipment_slot` IS NOT NULL)
                    ),

                CONSTRAINT `CK_items_equipment_set`
                    CHECK (`equipment_set` IS NULL OR `equipment_set` IN (1, 2)),

                CONSTRAINT `CK_items_equipment_slot`
                    CHECK (`equipment_slot` IS NULL OR `equipment_slot` BETWEEN 1 AND 16),

                CONSTRAINT `CK_items_alternate_equipment_slot`
                    CHECK (
                        `equipment_set` IS NULL
                        OR `equipment_set` <> 2
                        OR `equipment_slot` BETWEEN 1 AND 9
                    ),

                CONSTRAINT `CK_items_is_suspicious`
                    CHECK (`is_suspicious` IN (0, 1)),

                CONSTRAINT `CK_items_equipment_unlock_schedule`
                    CHECK (
                        ((`equipment_lock_state_mask` & 2) = 0 AND `equipment_unlock_at_utc` IS NULL)
                        OR
                        ((`equipment_lock_state_mask` & 2) = 2 AND `equipment_unlock_at_utc` IS NOT NULL)
                    ),

                CONSTRAINT `CK_items_stack_quantity`
                    CHECK (`stack_quantity` >= 1),

                KEY `IX_items_owner_character_id_location_kind`
                    (`owner_character_id`, `location_kind`),

                UNIQUE KEY `UX_items_owner_equipment_position`
                    (`owner_character_id`, `equipment_set`, `equipment_slot`)
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task CreateIncompatibleItemsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE `items`
            (
                `item_id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
                CONSTRAINT `PK_items` PRIMARY KEY (`item_id`)
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task AddLifetimeStateColumnAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items`
            ADD COLUMN `lifetime_state` TINYINT UNSIGNED NOT NULL AFTER `stack_quantity`
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task AddIncompatibleLifetimeStateColumnAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items`
            ADD COLUMN `lifetime_state` TINYINT NOT NULL AFTER `stack_quantity`
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task AddMisorderedLifetimeStateColumnAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items`
            ADD COLUMN `lifetime_state` TINYINT UNSIGNED NOT NULL AFTER `item_id`
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task CreateLifetimePersistenceStructureAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items`
                ADD COLUMN `lifetime_state` TINYINT UNSIGNED NOT NULL AFTER `stack_quantity`,
                ADD COLUMN `lifetime_duration_seconds` INT NULL AFTER `lifetime_state`,
                ADD COLUMN `lifetime_expires_at_utc` DATETIME(6) NULL AFTER `lifetime_duration_seconds`;

            CREATE INDEX `IX_items_lifetime_state_expires_at_utc`
                ON `items` (`lifetime_state`, `lifetime_expires_at_utc`);

            ALTER TABLE `items`
                ADD CONSTRAINT `CK_items_lifetime`
                CHECK (
                    (`lifetime_state` = 1
                        AND `lifetime_duration_seconds` IS NULL
                        AND `lifetime_expires_at_utc` IS NULL)
                    OR
                    (`lifetime_state` = 2
                        AND `lifetime_duration_seconds` IS NOT NULL
                        AND `lifetime_duration_seconds` > 0
                        AND `lifetime_expires_at_utc` IS NULL)
                    OR
                    (`lifetime_state` = 3
                        AND `lifetime_duration_seconds` IS NULL
                        AND `lifetime_expires_at_utc` IS NOT NULL)
                )
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task CreateIncompatibleLifetimePersistenceStructureAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items`
                ADD COLUMN `lifetime_state` TINYINT UNSIGNED NOT NULL AFTER `stack_quantity`,
                ADD COLUMN `lifetime_duration_seconds` INT NULL AFTER `lifetime_state`,
                ADD COLUMN `lifetime_expires_at_utc` DATETIME(6) NULL AFTER `lifetime_duration_seconds`;

            CREATE INDEX `IX_items_lifetime_state_expires_at_utc`
                ON `items` (`lifetime_state`, `lifetime_expires_at_utc`);

            ALTER TABLE `items`
                ADD CONSTRAINT `CK_items_lifetime`
                CHECK (`lifetime_state` IN (1, 2, 3))
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task DropLifetimePersistenceStructureAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE `items` DROP CHECK `CK_items_lifetime`;
            DROP INDEX `IX_items_lifetime_state_expires_at_utc` ON `items`;
            ALTER TABLE `items`
                DROP COLUMN `lifetime_expires_at_utc`,
                DROP COLUMN `lifetime_duration_seconds`,
                DROP COLUMN `lifetime_state`
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task DropItemsTableAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE `items`
            """;

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private static async Task<uint> InsertCharacterAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `characters`
                (`account_id`,
                 `name`,
                 `appearance_composite`,
                 `hair_composite`,
                 `level`,
                 `experience`,
                 `strength`,
                 `agility`,
                 `vitality`,
                 `spirit`,
                 `unspent_attribute_points`,
                 `current_life`,
                 `current_mana`,
                 `profession`,
                 `first_profession`,
                 `previous_profession`,
                 `rebirth_count`,
                 `pre_rebirth_level`,
                 `silver`,
                 `conquer_points`,
                 `bound_conquer_points`,
                 `pk_points`,
                 `title_id`,
                 `enlightenment_points`,
                 `map_id`,
                 `position_x`,
                 `position_y`)
            VALUES
                (1,
                 @name,
                 1003,
                 410,
                 1,
                 0,
                 10,
                 10,
                 10,
                 10,
                 0,
                 100,
                 0,
                 10,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 1002,
                 430,
                 378)
            """;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = "Migration" + Guid.NewGuid().ToString("N")[..6];

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private static async Task InsertV4ItemAsync(string connectionString, uint ownerCharacterId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `items`
                (`owner_character_id`,
                 `item_type_id`,
                 `location_kind`,
                 `equipment_set`,
                 `equipment_slot`,
                 `durability`,
                 `maximum_durability`,
                 `retail_compatibility_byte_a`,
                 `socket_progress_or_steed_color_or_monster_counter_baseline`,
                 `socket1_code`,
                 `socket2_code`,
                 `hidden_attack_effect`,
                 `retail_compatibility_byte_b`,
                 `addition_level`,
                 `damage_reduction_percent_or_steed_composition_red`,
                 `item_binding_code`,
                 `enchantment_life_bonus_or_steed_composition_green`,
                 `monster_restraint_id_or_steed_composition_blue`,
                 `is_suspicious`,
                 `equipment_lock_state_mask`,
                 `equipment_unlock_at_utc`,
                 `equipment_color`,
                 `composition_progress`,
                 `inscribed_syndicate_id`,
                 `stack_quantity`)
            VALUES
                (@owner_character_id,
                 100000,
                 1,
                 NULL,
                 NULL,
                 100,
                 100,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 NULL,
                 0,
                 0,
                 0,
                 1)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task InsertV5ItemAsync(string connectionString, uint ownerCharacterId)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `items`
                (`owner_character_id`,
                 `item_type_id`,
                 `location_kind`,
                 `equipment_set`,
                 `equipment_slot`,
                 `durability`,
                 `maximum_durability`,
                 `retail_compatibility_byte_a`,
                 `socket_progress_or_steed_color_or_monster_counter_baseline`,
                 `socket1_code`,
                 `socket2_code`,
                 `hidden_attack_effect`,
                 `retail_compatibility_byte_b`,
                 `addition_level`,
                 `damage_reduction_percent_or_steed_composition_red`,
                 `item_binding_code`,
                 `enchantment_life_bonus_or_steed_composition_green`,
                 `monster_restraint_id_or_steed_composition_blue`,
                 `is_suspicious`,
                 `equipment_lock_state_mask`,
                 `equipment_unlock_at_utc`,
                 `equipment_color`,
                 `composition_progress`,
                 `inscribed_syndicate_id`,
                 `stack_quantity`,
                 `lifetime_state`,
                 `lifetime_duration_seconds`,
                 `lifetime_expires_at_utc`)
            VALUES
                (@owner_character_id,
                 100000,
                 1,
                 NULL,
                 NULL,
                 100,
                 100,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 0,
                 NULL,
                 0,
                 0,
                 0,
                 1,
                 @lifetime_state,
                 NULL,
                 NULL)
            """;
        command.Parameters.Add("@owner_character_id", MySqlDbType.UInt32).Value = ownerCharacterId;
        command.Parameters.Add("@lifetime_state", MySqlDbType.UByte).Value = (byte)ItemLifetimeState.Permanent;

        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
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

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private static async Task AssertItemsTablePresentAsync(string connectionString)
    {
        Assert.Equal(1L, await ReadItemsTableCountAsync(connectionString));
    }

    private static async Task AssertItemsTableAbsentAsync(string connectionString)
    {
        Assert.Equal(0L, await ReadItemsTableCountAsync(connectionString));
    }

    private static async Task<long> ReadItemsTableCountAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `TABLE_TYPE` = 'BASE TABLE'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);

        return Convert.ToInt64(result);
    }

    private static async Task AssertItemColumnCountAsync(string connectionString, long expectedCount)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, Convert.ToInt64(result));
    }

    private static async Task AssertItemRowCountAsync(string connectionString, long expectedCount)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `items`
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(expectedCount, Convert.ToInt64(result));
    }

    private static async Task AssertLifetimePersistenceStructurePresentAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand columns = connection.CreateCommand();
        columns.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `COLUMN_NAME` IN
              (
                  'lifetime_state',
                  'lifetime_duration_seconds',
                  'lifetime_expires_at_utc'
              )
            """;

        object? columnCount = await columns.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(columnCount);
        Assert.Equal(3L, Convert.ToInt64(columnCount));

        await using MySqlCommand indexes = connection.CreateCommand();
        indexes.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            """;

        object? indexCount = await indexes.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(indexCount);
        Assert.Equal(2L, Convert.ToInt64(indexCount));

        await using MySqlCommand checks = connection.CreateCommand();
        checks.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `CONSTRAINT_TYPE` = 'CHECK'
              AND `ENFORCED` = 'YES'
              AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
            """;

        object? checkCount = await checks.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(checkCount);
        Assert.Equal(1L, Convert.ToInt64(checkCount));
    }

    private static async Task AssertLifetimePersistenceStructureAbsentAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand columns = connection.CreateCommand();
        columns.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `COLUMN_NAME` IN
              (
                  'lifetime_state',
                  'lifetime_duration_seconds',
                  'lifetime_expires_at_utc'
              )
            """;

        object? columnCount = await columns.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(columnCount);
        Assert.Equal(0L, Convert.ToInt64(columnCount));

        await using MySqlCommand indexes = connection.CreateCommand();
        indexes.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            """;

        object? indexCount = await indexes.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(indexCount);
        Assert.Equal(0L, Convert.ToInt64(indexCount));

        await using MySqlCommand checks = connection.CreateCommand();
        checks.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'items'
              AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
            """;

        object? checkCount = await checks.ExecuteScalarAsync(CancellationToken);
        Assert.NotNull(checkCount);
        Assert.Equal(0L, Convert.ToInt64(checkCount));
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
