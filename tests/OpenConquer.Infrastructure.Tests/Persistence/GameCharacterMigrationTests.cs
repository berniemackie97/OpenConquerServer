using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Extensions;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameCharacterMigrationTests
{
    private const string DatabaseName = "openconquer_game_character_migration_tests";
    private const string InitialMigrationId = "20260914210346_InitialGameSchema";
    private const string SignedPkPointsMigrationId = "20260922025854_UseSignedCharacterPkPoints";
    private const string PreRebirthLevelMigrationId = "20260923223920_RetainPreRebirthLevel";
    private const string SignedPkPointsGuard = "CK_characters_pk_points_signed_migration";
    private const string PreRebirthLevelConstraint = "CK_characters_pre_rebirth_level";
    private const string RebirthStateConstraint = "CK_characters_rebirth_state";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseSignedCharacterPkPoints_MigratesPopulatedV1SchemaWithoutLosingCharacter()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, InitialMigrationId);

        const uint accountId = 101;
        const int pkPoints = 321;

        await InsertLegacyCharacterAsync(connectionString, accountId, "SignedV1", rebirthCount: 0, pkPoints);

        await AssertCharacterPkPointsAsync(connectionString, accountId, pkPoints);
        await AssertPkPointsColumnAsync(connectionString, "smallint unsigned");
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 1, InitialMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId);

        await MigrateAsync(connectionString, SignedPkPointsMigrationId);

        await AssertCharacterPkPointsAsync(connectionString, accountId, pkPoints);
        await AssertPkPointsColumnAsync(connectionString, "smallint");
        await AssertConstraintAbsentAsync(connectionString, SignedPkPointsGuard);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 2, SignedPkPointsMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId, SignedPkPointsMigrationId);
    }

    [Fact]
    public async Task UseSignedCharacterPkPoints_WhenStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, InitialMigrationId);

        const uint accountId = 102;
        const int pkPoints = 654;

        await InsertLegacyCharacterAsync(connectionString, accountId, "SignedResume", rebirthCount: 0, pkPoints);
        await SimulateCommittedSignedPkPointsStructuralUpgradeWithoutMetadataAsync(connectionString);

        await AssertCharacterPkPointsAsync(connectionString, accountId, pkPoints);
        await AssertPkPointsColumnAsync(connectionString, "smallint");
        await AssertConstraintPresentAsync(connectionString, SignedPkPointsGuard);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 1, InitialMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId);

        await MigrateAsync(connectionString, SignedPkPointsMigrationId);

        await AssertCharacterPkPointsAsync(connectionString, accountId, pkPoints);
        await AssertPkPointsColumnAsync(connectionString, "smallint");
        await AssertConstraintAbsentAsync(connectionString, SignedPkPointsGuard);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 2, SignedPkPointsMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId, SignedPkPointsMigrationId);
    }

    [Fact]
    public async Task RetainPreRebirthLevel_MigratesPopulatedNonRebornV2SchemaAndCurrentRepositoryReadsProfile()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SignedPkPointsMigrationId);

        const uint accountId = 201;
        const int pkPoints = -123;

        await InsertLegacyCharacterAsync(connectionString, accountId, "NonRebornV2", rebirthCount: 0, pkPoints);

        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);

        await AssertPreRebirthLevelColumnAsync(connectionString, expectedNullable: "NO");
        await AssertConstraintPresentAsync(connectionString, PreRebirthLevelConstraint);
        await AssertConstraintPresentAsync(connectionString, RebirthStateConstraint);
        await AssertCharacterPreRebirthLevelAsync(connectionString, accountId, expectedLevel: 0);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId, SignedPkPointsMigrationId, PreRebirthLevelMigrationId);

        CharacterLoginProfile profile = await ReadCurrentProfileAsync(connectionString, accountId);

        Assert.Equal(accountId, profile.Identity.AccountId);
        Assert.Equal("NonRebornV2", profile.Identity.Name);
        Assert.Equal((byte)0, profile.Progression.RebirthCount);
        Assert.Equal((byte)0, profile.Progression.PreRebirthLevel);
        Assert.Equal((short)pkPoints, profile.PkPoints);
    }

    [Fact]
    public async Task RetainPreRebirthLevel_RebornCharacterWithoutRetainedLevelFailsClosedThenResumesAfterRepair()
    {
        await using MySqlContainer database = CreateDatabaseContainer();
        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);
        await MigrateAsync(connectionString, SignedPkPointsMigrationId);

        const uint nonRebornAccountId = 301;
        const uint rebornAccountId = 302;

        await InsertLegacyCharacterAsync(connectionString, nonRebornAccountId, "NonRebornV2", rebirthCount: 0, pkPoints: -12);
        await InsertLegacyCharacterAsync(connectionString, rebornAccountId, "RebornV2", rebirthCount: 2, pkPoints: -321);

        MySqlException migrationFailure = await Assert.ThrowsAsync<MySqlException>(() =>
            MigrateAsync(connectionString, PreRebirthLevelMigrationId));

        Assert.Equal(3819, migrationFailure.Number);

        await AssertPreRebirthLevelColumnAsync(connectionString, expectedNullable: "YES");
        await AssertCharacterPreRebirthLevelAsync(connectionString, nonRebornAccountId, expectedLevel: 0);
        await AssertCharacterPreRebirthLevelAsync(connectionString, rebornAccountId, expectedLevel: null);
        await AssertConstraintAbsentAsync(connectionString, PreRebirthLevelConstraint);
        await AssertConstraintAbsentAsync(connectionString, RebirthStateConstraint);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 2, SignedPkPointsMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId, SignedPkPointsMigrationId);

        await RepairPreRebirthLevelAsync(connectionString, rebornAccountId, retainedLevel: 130);

        await MigrateAsync(connectionString, PreRebirthLevelMigrationId);

        await AssertPreRebirthLevelColumnAsync(connectionString, expectedNullable: "NO");
        await AssertCharacterPreRebirthLevelAsync(connectionString, nonRebornAccountId, expectedLevel: 0);
        await AssertCharacterPreRebirthLevelAsync(connectionString, rebornAccountId, expectedLevel: 130);
        await AssertConstraintPresentAsync(connectionString, PreRebirthLevelConstraint);
        await AssertConstraintPresentAsync(connectionString, RebirthStateConstraint);
        await AssertCompatibilityAsync(connectionString, expectedSchemaVersion: 3, PreRebirthLevelMigrationId);
        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId, SignedPkPointsMigrationId, PreRebirthLevelMigrationId);

        CharacterLoginProfile nonRebornProfile = await ReadCurrentProfileAsync(connectionString, nonRebornAccountId);
        CharacterLoginProfile rebornProfile = await ReadCurrentProfileAsync(connectionString, rebornAccountId);

        Assert.Equal((byte)0, nonRebornProfile.Progression.RebirthCount);
        Assert.Equal((byte)0, nonRebornProfile.Progression.PreRebirthLevel);
        Assert.Equal((short)-12, nonRebornProfile.PkPoints);

        Assert.Equal(rebornAccountId, rebornProfile.Identity.AccountId);
        Assert.Equal("RebornV2", rebornProfile.Identity.Name);
        Assert.Equal((byte)2, rebornProfile.Progression.RebirthCount);
        Assert.Equal((byte)130, rebornProfile.Progression.PreRebirthLevel);
        Assert.Equal((short)-321, rebornProfile.PkPoints);
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

    private static async Task InsertLegacyCharacterAsync(string connectionString, uint accountId, string name, byte rebirthCount, int pkPoints)
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
                (@account_id,
                 @name,
                 2002001,
                 456,
                 140,
                 12345678901,
                 101,
                 102,
                 103,
                 104,
                 105,
                 106,
                 107,
                 135,
                 10,
                 20,
                 @rebirth_count,
                 1234567890,
                 2345678901,
                 3456789012,
                 @pk_points,
                 654,
                 987,
                 1002,
                 431,
                 379)
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;
        command.Parameters.Add("@name", MySqlDbType.VarChar).Value = name;
        command.Parameters.Add("@rebirth_count", MySqlDbType.UByte).Value = rebirthCount;
        command.Parameters.Add("@pk_points", MySqlDbType.Int32).Value = pkPoints;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private static async Task SimulateCommittedSignedPkPointsStructuralUpgradeWithoutMetadataAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using (MySqlCommand addGuard = connection.CreateCommand())
        {
            addGuard.CommandText = $"""
                ALTER TABLE `characters`
                    ADD CONSTRAINT `{SignedPkPointsGuard}`
                    CHECK (`pk_points` <= 32767)
                """;

            await addGuard.ExecuteNonQueryAsync(CancellationToken);
        }

        await using (MySqlCommand convertColumn = connection.CreateCommand())
        {
            convertColumn.CommandText = """
                ALTER TABLE `characters`
                    MODIFY COLUMN `pk_points` SMALLINT NOT NULL
                """;

            await convertColumn.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    private static async Task RepairPreRebirthLevelAsync(string connectionString, uint accountId, byte retainedLevel)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE `characters`
            SET `pre_rebirth_level` = @pre_rebirth_level
            WHERE `account_id` = @account_id
            """;

        command.Parameters.Add("@pre_rebirth_level", MySqlDbType.UByte).Value = retainedLevel;
        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);
    }

    private static async Task<CharacterLoginProfile> ReadCurrentProfileAsync(string connectionString, uint accountId)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddGamePersistence(connectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        ICharacterLoginProfileRepository repository = services.GetRequiredService<ICharacterLoginProfileRepository>();

        return Assert.IsType<CharacterLoginProfile>(await repository.FindByAccountIdAsync(accountId, CancellationToken));
    }

    private static async Task AssertCharacterPkPointsAsync(string connectionString, uint accountId, int expectedPkPoints)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `pk_points`
            FROM `characters`
            WHERE `account_id` = @account_id
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.NotEqual(DBNull.Value, result);
        Assert.Equal(expectedPkPoints, Convert.ToInt32(result));
    }

    private static async Task AssertCharacterPreRebirthLevelAsync(string connectionString, uint accountId, byte? expectedLevel)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `pre_rebirth_level`
            FROM `characters`
            WHERE `account_id` = @account_id
            """;

        command.Parameters.Add("@account_id", MySqlDbType.UInt32).Value = accountId;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);

        if (expectedLevel is null)
        {
            Assert.Equal(DBNull.Value, result);
            return;
        }

        Assert.NotEqual(DBNull.Value, result);
        Assert.Equal(expectedLevel.Value, Convert.ToByte(result));
    }

    private static async Task AssertPkPointsColumnAsync(string connectionString, string expectedColumnType)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_TYPE`, `IS_NULLABLE`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `COLUMN_NAME` = 'pk_points'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(expectedColumnType, reader.GetString(0));
        Assert.Equal("NO", reader.GetString(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertPreRebirthLevelColumnAsync(string connectionString, string expectedNullable)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT `COLUMN_TYPE`, `IS_NULLABLE`, `COLUMN_DEFAULT`, `EXTRA`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `COLUMN_NAME` = 'pre_rebirth_level'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("tinyint unsigned", reader.GetString(0));
        Assert.Equal(expectedNullable, reader.GetString(1));
        Assert.True(reader.IsDBNull(2));
        Assert.Equal(string.Empty, reader.GetString(3));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertConstraintPresentAsync(string connectionString, string constraintName)
    {
        Assert.Equal(1L, await ReadConstraintCountAsync(connectionString, constraintName));
    }

    private static async Task AssertConstraintAbsentAsync(string connectionString, string constraintName)
    {
        Assert.Equal(0L, await ReadConstraintCountAsync(connectionString, constraintName));
    }

    private static async Task<long> ReadConstraintCountAsync(string connectionString, string constraintName)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'characters'
              AND `CONSTRAINT_TYPE` = 'CHECK'
              AND `CONSTRAINT_NAME` = @constraint_name
              AND `ENFORCED` = 'YES'
            """;

        command.Parameters.Add("@constraint_name", MySqlDbType.VarChar).Value = constraintName;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);

        return Convert.ToInt64(result);
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
