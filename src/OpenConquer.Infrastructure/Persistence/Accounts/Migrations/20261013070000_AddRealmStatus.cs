using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20261013070000_AddRealmStatus")]
public sealed class AddRealmStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE `realm_status`
            (
                `realm_id` INT UNSIGNED NOT NULL,
                `load_state` TINYINT UNSIGNED NOT NULL,
                `online_count` INT UNSIGNED NOT NULL,
                `reported_at_utc` DATETIME(6) NOT NULL,
                `expires_at_utc` DATETIME(6) NOT NULL,
                CONSTRAINT `PK_realm_status` PRIMARY KEY (`realm_id`),
                CONSTRAINT `FK_realm_status_realms_realm_id` FOREIGN KEY (`realm_id`) REFERENCES `realms` (`realm_id`) ON DELETE CASCADE,
                CONSTRAINT `CK_realm_status_load_state` CHECK (`load_state` IN (1, 2, 3)),
                CONSTRAINT `CK_realm_status_lifetime` CHECK (`expires_at_utc` > `reported_at_utc`)
            ) ENGINE=InnoDB DEFAULT CHARSET=ascii COLLATE=ascii_bin;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 5,
                `migration_id` = '20261013070000_AddRealmStatus',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 4
              AND `migration_id` = '20261013050000_BindGameLoginTicketsToRealm';
            """,
            suppressTransaction: true);
    }

    // Status reports are short-lived runtime state that GameServers republish, so
    // removing them loses nothing an operator owns.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 4,
                `migration_id` = '20261013050000_BindGameLoginTicketsToRealm',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 5
              AND `migration_id` = '20261013070000_AddRealmStatus';
            """,
            suppressTransaction: true);

        migrationBuilder.Sql("DROP TABLE `realm_status`;", suppressTransaction: true);
    }
}
