using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Infrastructure.Persistence;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Infrastructure.Persistence.Schema;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(AccountSchemaDatabaseCollection.Name)]
public sealed class AccountDatabaseReadinessVerifierTests(AccountDatabaseFixture database)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private AccountDatabaseReadinessVerifier Verifier =>
        database.Services.GetRequiredService<AccountDatabaseReadinessVerifier>();

    [Fact]
    public async Task VerifyAsync_AcceptsProvisionedProductionSchema()
    {
        await Verifier.VerifyAsync(CancellationToken);
    }

    [Theory]
    [InlineData("utf8mb4", "utf8mb4_0900_ai_ci")]
    [InlineData("latin1", "latin1_swedish_ci")]
    public async Task VerifyAsync_RejectsIncompatibleDatabaseDefaults(
        string characterSet,
        string collation
    )
    {
        await SetDatabaseDefaultsAsync(characterSet, collation);

        try
        {
            await Assert.ThrowsAsync<AccountDatabaseReadinessException>(() =>
                Verifier.VerifyAsync(CancellationToken)
            );
        }
        finally
        {
            await SetDatabaseDefaultsAsync(
                AccountSchemaContract.DatabaseCharacterSet,
                AccountSchemaContract.DatabaseCollation
            );
        }
    }

    [Fact]
    public async Task VerifyAsync_RejectsMissingSchemaCompatibilityMarker()
    {
        SchemaCompatibilityRecord original = await ReadCompatibilityAsync();

        await ExecuteAdministrativeCommandAsync(
            """
            DELETE FROM `schema_compatibility`
            WHERE `component_name` = @component_name
            """,
            new MySqlParameter("@component_name", AccountSchemaContract.ComponentName)
        );

        try
        {
            await Assert.ThrowsAsync<AccountDatabaseReadinessException>(() =>
                Verifier.VerifyAsync(CancellationToken)
            );
        }
        finally
        {
            await ExecuteAdministrativeCommandAsync(
                """
                INSERT INTO `schema_compatibility`
                    (`component_name`,
                     `schema_version`,
                     `migration_id`,
                     `applied_at_utc`)
                VALUES
                    (@component_name,
                     @schema_version,
                     @migration_id,
                     @applied_at_utc)
                """,
                new MySqlParameter("@component_name", original.ComponentName),
                new MySqlParameter("@schema_version", original.SchemaVersion),
                new MySqlParameter("@migration_id", original.MigrationId),
                new MySqlParameter("@applied_at_utc", original.AppliedAtUtc)
            );
        }
    }

    [Fact]
    public async Task VerifyAsync_RejectsWrongSchemaVersion()
    {
        await SetSchemaVersionAsync(AccountSchemaContract.SchemaVersion + 1);

        try
        {
            await Assert.ThrowsAsync<AccountDatabaseReadinessException>(() =>
                Verifier.VerifyAsync(CancellationToken)
            );
        }
        finally
        {
            await SetSchemaVersionAsync(AccountSchemaContract.SchemaVersion);
        }
    }

    [Fact]
    public async Task VerifyAsync_RejectsWrongMigrationIdentity()
    {
        await SetMigrationIdAsync("unexpected_migration");

        try
        {
            await Assert.ThrowsAsync<AccountDatabaseReadinessException>(() =>
                Verifier.VerifyAsync(CancellationToken)
            );
        }
        finally
        {
            await SetMigrationIdAsync(AccountSchemaContract.MigrationId);
        }
    }

    [Fact]
    public async Task VerifyAsync_ObservesCallerCancellation()
    {
        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Verifier.VerifyAsync(cancellation.Token)
        );
    }

    [Fact]
    public async Task RuntimeIdentity_CannotCreateSchemaObjects()
    {
        await using MySqlConnection connection = new(database.RuntimeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand createTable = connection.CreateCommand();

        createTable.CommandText = """
            CREATE TABLE `runtime_ddl_probe`
            (
                `id` INT NOT NULL
            )
            """;

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            createTable.ExecuteNonQueryAsync(CancellationToken)
        );

        Assert.Equal(1142, exception.Number);
    }

    [Fact]
    public async Task RuntimeIdentity_CannotModifySchemaCompatibility()
    {
        await using MySqlConnection connection = new(database.RuntimeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand updateCompatibility = connection.CreateCommand();

        updateCompatibility.CommandText = """
            UPDATE `schema_compatibility`
            SET `schema_version` = `schema_version` + 1
            WHERE `component_name` = @component_name
            """;

        updateCompatibility.Parameters.AddWithValue(
            "@component_name",
            AccountSchemaContract.ComponentName
        );

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            updateCompatibility.ExecuteNonQueryAsync(CancellationToken)
        );

        Assert.Equal(1142, exception.Number);
    }

    private async Task<SchemaCompatibilityRecord> ReadCompatibilityAsync()
    {
        await using AccountDbContext db = await database.ContextFactory.CreateDbContextAsync(
            CancellationToken
        );

        return await db
            .SchemaCompatibility.AsNoTracking()
            .SingleAsync(
                candidate => candidate.ComponentName == AccountSchemaContract.ComponentName,
                CancellationToken
            );
    }

    private async Task SetSchemaVersionAsync(uint schemaVersion)
    {
        await ExecuteAdministrativeCommandAsync(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = @schema_version
            WHERE `component_name` = @component_name
            """,
            new MySqlParameter("@schema_version", schemaVersion),
            new MySqlParameter("@component_name", AccountSchemaContract.ComponentName)
        );
    }

    private async Task SetMigrationIdAsync(string migrationId)
    {
        await ExecuteAdministrativeCommandAsync(
            """
            UPDATE `schema_compatibility`
            SET `migration_id` = @migration_id
            WHERE `component_name` = @component_name
            """,
            new MySqlParameter("@migration_id", migrationId),
            new MySqlParameter("@component_name", AccountSchemaContract.ComponentName)
        );
    }

    private async Task SetDatabaseDefaultsAsync(string characterSet, string collation)
    {
        string sql = $"""
            ALTER DATABASE `{AccountDatabaseFixture.DatabaseName}`
            CHARACTER SET {characterSet}
            COLLATE {collation}
            """;

        await ExecuteAdministrativeCommandAsync(sql);
    }

    private async Task ExecuteAdministrativeCommandAsync(
        string commandText,
        params MySqlParameter[] parameters
    )
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = new(commandText, connection);

        if (parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
