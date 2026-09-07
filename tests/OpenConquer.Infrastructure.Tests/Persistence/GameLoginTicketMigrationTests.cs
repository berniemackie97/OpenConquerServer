using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using OpenConquer.Infrastructure.Persistence;
using Testcontainers.MySql;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class GameLoginTicketMigrationTests
{
    private const string DatabaseName = "openconquer_accounts_migration_tests";

    private const string InitialMigrationId = "20260906043138_InitialAccountSchema";

    private const string ProtectedTicketMigrationId =
        "20260907004603_ProtectGameLoginTicketAuthenticationKey";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProtectGameLoginTicketAuthenticationKey_MigratesPopulatedV1Schema()
    {
        await using MySqlContainer database = CreateDatabaseContainer();

        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);

        await MigrateAsync(connectionString, InitialMigrationId);

        await AssertCompatibilityAsync(
            connectionString,
            expectedSchemaVersion: 1,
            InitialMigrationId
        );

        const uint sessionUid = 0x1020_3040u;
        const uint authenticationKey = 0x5060_7080u;

        await InsertLegacyTicketAsync(connectionString, sessionUid, authenticationKey);

        await AssertLegacyTicketExistsAsync(connectionString, sessionUid, authenticationKey);

        await MigrateAsync(connectionString, ProtectedTicketMigrationId);

        await AssertTicketTableIsEmptyAsync(connectionString);

        await AssertProtectedCredentialColumnsAsync(connectionString);

        await AssertCompatibilityAsync(
            connectionString,
            expectedSchemaVersion: 2,
            ProtectedTicketMigrationId
        );

        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            ProtectedTicketMigrationId
        );
    }

    [Fact]
    public async Task ProtectGameLoginTicketAuthenticationKey_WhenStructuralUpgradeCommittedWithoutMetadata_ResumesSafely()
    {
        await using MySqlContainer database = CreateDatabaseContainer();

        await database.StartAsync(CancellationToken);

        string connectionString = database.GetConnectionString();

        await ConfigureDatabaseAsync(connectionString);

        await MigrateAsync(connectionString, InitialMigrationId);

        const uint sessionUid = 0x1122_3344u;
        const uint authenticationKey = 0x5566_7788u;

        await InsertLegacyTicketAsync(connectionString, sessionUid, authenticationKey);

        await AssertLegacyTicketExistsAsync(connectionString, sessionUid, authenticationKey);

        await SimulateCommittedStructuralUpgradeWithoutMetadataAsync(connectionString);

        await AssertTicketTableIsEmptyAsync(connectionString);

        await AssertProtectedCredentialColumnsAsync(connectionString);

        await AssertCompatibilityAsync(
            connectionString,
            expectedSchemaVersion: 1,
            InitialMigrationId
        );

        await AssertMigrationHistoryAsync(connectionString, InitialMigrationId);

        await MigrateAsync(connectionString, ProtectedTicketMigrationId);

        await AssertTicketTableIsEmptyAsync(connectionString);

        await AssertProtectedCredentialColumnsAsync(connectionString);

        await AssertCompatibilityAsync(
            connectionString,
            expectedSchemaVersion: 2,
            ProtectedTicketMigrationId
        );

        await AssertMigrationHistoryAsync(
            connectionString,
            InitialMigrationId,
            ProtectedTicketMigrationId
        );
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
        await using AccountDbContext db = CreateDbContext(connectionString);

        await db.Database.ExecuteSqlRawAsync(
            $"""
            ALTER DATABASE `{DatabaseName}`
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs
            """,
            CancellationToken
        );
    }

    private static async Task MigrateAsync(string connectionString, string migrationId)
    {
        await using AccountDbContext db = CreateDbContext(connectionString);

        IMigrator migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(migrationId, CancellationToken);
    }

    private static AccountDbContext CreateDbContext(string connectionString)
    {
        DbContextOptionsBuilder<AccountDbContext> options = new();

        AccountDbContextOptionsConfiguration.Configure(options, connectionString);

        return new AccountDbContext(options.Options);
    }

    private static async Task InsertLegacyTicketAsync(
        string connectionString,
        uint sessionUid,
        uint authenticationKey
    )
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(
            CancellationToken
        );

        uint accountId;

        await using (MySqlCommand insertAccount = connection.CreateCommand())
        {
            insertAccount.Transaction = transaction;

            insertAccount.CommandText = """
                INSERT INTO `accounts`
                    (`username`,
                     `access_status`,
                     `authority_role`,
                     `creation_operation_id`,
                     `last_successful_login_at_utc`,
                     `state_revision`,
                     `created_at_utc`,
                     `created_by_actor_kind`,
                     `created_by_account_id`,
                     `state_changed_at_utc`,
                     `state_changed_by_actor_kind`,
                     `state_changed_by_account_id`,
                     `deleted_at_utc`,
                     `deleted_by_actor_kind`,
                     `deleted_by_account_id`)
                VALUES
                    ('MigrationTicket',
                     1,
                     1,
                     UUID_TO_BIN(UUID()),
                     NULL,
                     1,
                     '2026-01-01 00:00:00',
                     3,
                     NULL,
                     '2026-01-01 00:00:00',
                     3,
                     NULL,
                     NULL,
                     NULL,
                     NULL)
                """;

            int affected = await insertAccount.ExecuteNonQueryAsync(CancellationToken);

            Assert.Equal(1, affected);

            accountId = checked((uint)insertAccount.LastInsertedId);
        }

        await using (MySqlCommand insertTicket = connection.CreateCommand())
        {
            insertTicket.Transaction = transaction;

            insertTicket.CommandText = """
                INSERT INTO `game_login_tickets`
                    (`session_uid`,
                     `authentication_key`,
                     `account_id`,
                     `username`,
                     `issued_at_utc`,
                     `expires_at_utc`)
                VALUES
                    (@session_uid,
                     @authentication_key,
                     @account_id,
                     'MigrationTicket',
                     '2026-01-01 00:01:00',
                     '2026-01-01 00:06:00')
                """;

            insertTicket.Parameters.AddWithValue("@session_uid", sessionUid);

            insertTicket.Parameters.AddWithValue("@authentication_key", authenticationKey);

            insertTicket.Parameters.AddWithValue("@account_id", accountId);

            int affected = await insertTicket.ExecuteNonQueryAsync(CancellationToken);

            Assert.Equal(1, affected);
        }

        await transaction.CommitAsync(CancellationToken);
    }

    private static async Task SimulateCommittedStructuralUpgradeWithoutMetadataAsync(
        string connectionString
    )
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using (MySqlCommand deleteTickets = connection.CreateCommand())
        {
            deleteTickets.CommandText = """
                DELETE FROM `game_login_tickets`
                """;

            await deleteTickets.ExecuteNonQueryAsync(CancellationToken);
        }

        await using (MySqlCommand alterTickets = connection.CreateCommand())
        {
            alterTickets.CommandText = """
                ALTER TABLE `game_login_tickets`
                    DROP COLUMN `authentication_key`,
                    ADD COLUMN `authentication_key_verifier` BINARY(32) NOT NULL,
                    ADD COLUMN `authentication_key_verifier_key_id` SMALLINT UNSIGNED NOT NULL,
                    ADD CONSTRAINT `CK_game_login_tickets_session_uid`
                        CHECK (`session_uid` > 0),
                    ADD CONSTRAINT `CK_game_login_tickets_verifier_key_id`
                        CHECK (`authentication_key_verifier_key_id` > 0)
                """;

            await alterTickets.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    private static async Task AssertLegacyTicketExistsAsync(
        string connectionString,
        uint sessionUid,
        uint authenticationKey
    )
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT `authentication_key`
            FROM `game_login_tickets`
            WHERE `session_uid` = @session_uid
            """;

        command.Parameters.AddWithValue("@session_uid", sessionUid);

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(authenticationKey, Convert.ToUInt32(result));
    }

    private static async Task AssertTicketTableIsEmptyAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM `game_login_tickets`
            """;

        object? result = await command.ExecuteScalarAsync(CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(0L, Convert.ToInt64(result));
    }

    private static async Task AssertProtectedCredentialColumnsAsync(string connectionString)
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                `COLUMN_NAME`,
                `COLUMN_TYPE`,
                `IS_NULLABLE`,
                `COLUMN_DEFAULT`
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'game_login_tickets'
              AND `COLUMN_NAME` IN
                  ('authentication_key',
                   'authentication_key_verifier',
                   'authentication_key_verifier_key_id')
            ORDER BY `COLUMN_NAME`
            """;

        List<(string Name, string ColumnType, string IsNullable, bool HasDefault)> actual = [];

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add(
                (reader.GetString(0), reader.GetString(1), reader.GetString(2), !reader.IsDBNull(3))
            );
        }

        (string Name, string ColumnType, string IsNullable, bool HasDefault)[] expected =
        [
            ("authentication_key_verifier", "binary(32)", "NO", false),
            ("authentication_key_verifier_key_id", "smallint unsigned", "NO", false),
        ];

        Assert.Equal(expected, actual);
    }

    private static async Task AssertCompatibilityAsync(
        string connectionString,
        uint expectedSchemaVersion,
        string expectedMigrationId
    )
    {
        await using MySqlConnection connection = new(connectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                `schema_version`,
                `migration_id`
            FROM `schema_compatibility`
            WHERE `component_name` = 'accounts'
            """;

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        Assert.True(await reader.ReadAsync(CancellationToken));

        Assert.Equal(expectedSchemaVersion, reader.GetUInt32(0));

        Assert.Equal(expectedMigrationId, reader.GetString(1));

        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private static async Task AssertMigrationHistoryAsync(
        string connectionString,
        params string[] expected
    )
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
