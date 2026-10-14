using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20261013090000_AddAccountSessions")]
public sealed class AddAccountSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE `account_sessions`
            (
                `account_id` INT UNSIGNED NOT NULL,
                `realm_id` INT UNSIGNED NOT NULL,
                `character_id` INT UNSIGNED NOT NULL,
                `session_uid` INT UNSIGNED NOT NULL,
                `entered_at_utc` DATETIME(6) NOT NULL,
                CONSTRAINT `PK_account_sessions` PRIMARY KEY (`account_id`),
                CONSTRAINT `FK_account_sessions_accounts_account_id` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`account_id`) ON DELETE CASCADE,
                CONSTRAINT `FK_account_sessions_realms_realm_id` FOREIGN KEY (`realm_id`) REFERENCES `realms` (`realm_id`) ON DELETE CASCADE,
                CONSTRAINT `CK_account_sessions_character_id` CHECK (`character_id` > 0),
                CONSTRAINT `CK_account_sessions_session_uid` CHECK (`session_uid` > 0),
                INDEX `IX_account_sessions_realm_id` (`realm_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=ascii COLLATE=ascii_bin;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            CREATE TABLE `account_session_kicks`
            (
                `realm_id` INT UNSIGNED NOT NULL,
                `account_id` INT UNSIGNED NOT NULL,
                `requested_at_utc` DATETIME(6) NOT NULL,
                CONSTRAINT `PK_account_session_kicks` PRIMARY KEY (`realm_id`, `account_id`),
                CONSTRAINT `FK_account_session_kicks_realms_realm_id` FOREIGN KEY (`realm_id`) REFERENCES `realms` (`realm_id`) ON DELETE CASCADE,
                CONSTRAINT `FK_account_session_kicks_accounts_account_id` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`account_id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=ascii COLLATE=ascii_bin;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 6,
                `migration_id` = '20261013090000_AddAccountSessions',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 5
              AND `migration_id` = '20261013070000_AddRealmStatus';
            """,
            suppressTransaction: true);
    }

    // Both tables hold only live login state that GameServers clear and rebuild at startup.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 5,
                `migration_id` = '20261013070000_AddRealmStatus',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 6
              AND `migration_id` = '20261013090000_AddAccountSessions';
            """,
            suppressTransaction: true);

        migrationBuilder.Sql("DROP TABLE `account_session_kicks`;", suppressTransaction: true);
        migrationBuilder.Sql("DROP TABLE `account_sessions`;", suppressTransaction: true);
    }
}
