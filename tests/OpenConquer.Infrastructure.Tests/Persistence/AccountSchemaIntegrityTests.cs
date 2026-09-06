using MySqlConnector;

namespace OpenConquer.Infrastructure.Tests.Persistence;

[Collection(AccountSchemaDatabaseCollection.Name)]
public sealed class AccountSchemaIntegrityTests(AccountDatabaseFixture database)
{
    private static readonly string[] s_expectedCheckConstraints =
    [
        "CK_accounts_access_status",
        "CK_accounts_authority_role",
        "CK_accounts_created_actor",
        "CK_accounts_creation_operation_id",
        "CK_accounts_deleted_state",
        "CK_accounts_last_successful_login_at",
        "CK_accounts_state_changed_actor",
        "CK_accounts_state_changed_at",
        "CK_accounts_state_revision",
        "CK_account_audit_events_actor",
        "CK_account_audit_events_correlation_id",
        "CK_account_audit_events_event_kind",
        "CK_account_audit_events_new_access_status",
        "CK_account_audit_events_new_authority_role",
        "CK_account_audit_events_payload",
        "CK_account_audit_events_previous_access_status",
        "CK_account_audit_events_previous_authority_role",
        "CK_account_audit_events_reason_code",
        "CK_account_password_credentials_password_hash",
        "CK_account_password_credentials_revision",
        "CK_game_login_tickets_expiration",
        "CK_schema_compatibility_component_name",
        "CK_schema_compatibility_migration_id",
        "CK_schema_compatibility_schema_version",
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string?, string> AccountConstraintCases =>
        new()
        {
            {
                "CK_accounts_access_status",
                """
                    UPDATE `accounts`
                    SET `access_status` = 0
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_authority_role",
                """
                    UPDATE `accounts`
                    SET `authority_role` = 0
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_creation_operation_id",
                """
                    UPDATE `accounts`
                    SET `creation_operation_id` =
                        0x00000000000000000000000000000000
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_state_revision",
                """
                    UPDATE `accounts`
                    SET `state_revision` = 0
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_created_actor",
                """
                    UPDATE `accounts`
                    SET `created_by_actor_kind` = 2,
                        `created_by_account_id` = NULL
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_state_changed_actor",
                """
                    UPDATE `accounts`
                    SET `state_changed_by_actor_kind` = 1,
                        `state_changed_by_account_id` = NULL
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_deleted_state",
                """
                    UPDATE `accounts`
                    SET `deleted_at_utc` = '2026-01-01 00:01:00'
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_deleted_state",
                """
                    UPDATE `accounts`
                    SET `deleted_at_utc` = '2026-01-01 00:01:00',
                        `deleted_by_actor_kind` = 3,
                        `deleted_by_account_id` = NULL
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_deleted_state",
                """
                    UPDATE `accounts`
                    SET `deleted_at_utc` = `state_changed_at_utc`,
                        `deleted_by_actor_kind` = 4,
                        `deleted_by_account_id` = NULL
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_last_successful_login_at",
                """
                    UPDATE `accounts`
                    SET `last_successful_login_at_utc` =
                        '2025-12-31 23:59:59'
                    WHERE `account_id` = @account_id
                    """
            },
            {
                "CK_accounts_state_changed_at",
                """
                    UPDATE `accounts`
                    SET `state_changed_at_utc` =
                        '2025-12-31 23:59:59'
                    WHERE `account_id` = @account_id
                    """
            },
        };

    public static TheoryData<string?, string> PasswordCredentialConstraintCases =>
        new()
        {
            {
                "CK_account_password_credentials_password_hash",
                """
                    INSERT INTO `account_password_credentials`
                        (`account_id`,
                         `password_hash`,
                         `password_changed_at_utc`,
                         `revision`)
                    VALUES
                        (@account_id,
                         '',
                         '2026-01-01 00:00:00',
                         1)
                    """
            },
            {
                "CK_account_password_credentials_revision",
                """
                    INSERT INTO `account_password_credentials`
                        (`account_id`,
                         `password_hash`,
                         `password_changed_at_utc`,
                         `revision`)
                    VALUES
                        (@account_id,
                         '$openconquer$test$hash',
                         '2026-01-01 00:00:00',
                         0)
                    """
            },
        };

    public static TheoryData<string?, string> AuditConstraintCases =>
        new()
        {
            {
                null,
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         0,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         NULL,
                         NULL)
                    """
            },
            {
                "CK_account_audit_events_actor",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         8,
                         '2026-01-01 00:01:00',
                         1,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         NULL,
                         NULL)
                    """
            },
            {
                "CK_account_audit_events_correlation_id",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         8,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         0x00000000000000000000000000000000,
                         NULL,
                         NULL,
                         NULL,
                         NULL,
                         NULL)
                    """
            },
            {
                "CK_account_audit_events_reason_code",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         8,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         '',
                         NULL,
                         NULL,
                         NULL,
                         NULL)
                    """
            },
            {
                null,
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         2,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         99,
                         2,
                         NULL,
                         NULL)
                    """
            },
            {
                null,
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         2,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         1,
                         99,
                         NULL,
                         NULL)
                    """
            },
            {
                null,
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         10,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         99,
                         2)
                    """
            },
            {
                null,
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         10,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         1,
                         99)
                    """
            },
            {
                "CK_account_audit_events_payload",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         1,
                         '2026-01-01 00:01:00',
                         1,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         NULL,
                         1)
                    """
            },
            {
                "CK_account_audit_events_payload",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         2,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         2,
                         NULL,
                         NULL)
                    """
            },
            {
                "CK_account_audit_events_payload",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         10,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         1,
                         NULL)
                    """
            },
            {
                "CK_account_audit_events_payload",
                """
                    INSERT INTO `account_audit_events`
                        (`account_id`,
                         `event_kind`,
                         `occurred_at_utc`,
                         `actor_kind`,
                         `actor_account_id`,
                         `correlation_id`,
                         `reason_code`,
                         `previous_access_status`,
                         `new_access_status`,
                         `previous_authority_role`,
                         `new_authority_role`)
                    VALUES
                        (@account_id,
                         10,
                         '2026-01-01 00:01:00',
                         3,
                         NULL,
                         UUID_TO_BIN(UUID()),
                         NULL,
                         NULL,
                         NULL,
                         1,
                         1)
                    """
            },
        };

    public static TheoryData<string?, string> SchemaCompatibilityConstraintCases =>
        new()
        {
            {
                "CK_schema_compatibility_component_name",
                """
                    UPDATE `schema_compatibility`
                    SET `component_name` = ''
                    WHERE `component_name` = 'accounts'
                    """
            },
            {
                "CK_schema_compatibility_schema_version",
                """
                    UPDATE `schema_compatibility`
                    SET `schema_version` = 0
                    WHERE `component_name` = 'accounts'
                    """
            },
            {
                "CK_schema_compatibility_migration_id",
                """
                    UPDATE `schema_compatibility`
                    SET `migration_id` = ''
                    WHERE `component_name` = 'accounts'
                    """
            },
        };

    [Fact]
    public async Task CheckConstraintMetadata_ContainsExpectedContract()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
            SELECT `CONSTRAINT_NAME`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `CONSTRAINT_TYPE` = 'CHECK'
            ORDER BY `CONSTRAINT_NAME`
            """;

        List<string> actual = [];

        await using MySqlDataReader reader = await command.ExecuteReaderAsync(CancellationToken);

        while (await reader.ReadAsync(CancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(
            s_expectedCheckConstraints.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal)
        );
    }

    [Theory]
    [MemberData(nameof(AccountConstraintCases))]
    public async Task Accounts_CheckConstraintsRejectInvalidState(
        string? expectedConstraint,
        string commandText
    )
    {
        uint accountId = await InsertReferenceAccountAsync();

        await AssertCheckConstraintViolationAsync(
            expectedConstraint,
            commandText,
            new MySqlParameter("@account_id", accountId)
        );
    }

    [Theory]
    [MemberData(nameof(PasswordCredentialConstraintCases))]
    public async Task AccountPasswordCredentials_CheckConstraintsRejectInvalidState(
        string? expectedConstraint,
        string commandText
    )
    {
        uint accountId = await InsertReferenceAccountAsync();

        await AssertCheckConstraintViolationAsync(
            expectedConstraint,
            commandText,
            new MySqlParameter("@account_id", accountId)
        );
    }

    [Theory]
    [MemberData(nameof(AuditConstraintCases))]
    public async Task AccountAuditEvents_CheckConstraintsRejectInvalidState(
        string? expectedConstraint,
        string commandText
    )
    {
        uint accountId = await InsertReferenceAccountAsync();

        await AssertCheckConstraintViolationAsync(
            expectedConstraint,
            commandText,
            new MySqlParameter("@account_id", accountId)
        );
    }

    [Fact]
    public async Task GameLoginTickets_RequireStrictlyIncreasingExpiration()
    {
        uint accountId = await InsertReferenceAccountAsync();

        await AssertCheckConstraintViolationAsync(
            "CK_game_login_tickets_expiration",
            """
            INSERT INTO `game_login_tickets`
                (`session_uid`,
                 `authentication_key`,
                 `account_id`,
                 `username`,
                 `issued_at_utc`,
                 `expires_at_utc`)
            VALUES
                (@account_id,
                 1,
                 @account_id,
                 @username,
                 '2026-01-01 00:01:00',
                 '2026-01-01 00:01:00')
            """,
            new MySqlParameter("@account_id", accountId),
            new MySqlParameter("@username", CreateUsername())
        );
    }

    [Theory]
    [MemberData(nameof(SchemaCompatibilityConstraintCases))]
    public async Task SchemaCompatibility_CheckConstraintsRejectInvalidState(
        string? expectedConstraint,
        string commandText
    )
    {
        await AssertCheckConstraintViolationAsync(expectedConstraint, commandText);
    }

    private async Task<uint> InsertReferenceAccountAsync()
    {
        await using MySqlConnection connection = new(database.AdministrativeConnectionString);

        await connection.OpenAsync(CancellationToken);

        await using MySqlCommand command = connection.CreateCommand();

        command.CommandText = """
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
                (@username,
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

        command.Parameters.AddWithValue("@username", CreateUsername());

        int affected = await command.ExecuteNonQueryAsync(CancellationToken);

        Assert.Equal(1, affected);

        return checked((uint)command.LastInsertedId);
    }

    private async Task AssertCheckConstraintViolationAsync(
        string? expectedConstraint,
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

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            command.ExecuteNonQueryAsync(CancellationToken)
        );

        Assert.Equal(3819, exception.Number);

        if (expectedConstraint is not null)
        {
            Assert.Contains(expectedConstraint, exception.Message);
        }
    }

    private static string CreateUsername()
    {
        return "Schema" + Guid.NewGuid().ToString("N")[..26];
    }
}
