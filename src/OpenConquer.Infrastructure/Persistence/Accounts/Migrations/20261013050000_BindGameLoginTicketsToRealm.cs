using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20261013050000_BindGameLoginTicketsToRealm")]
public sealed class BindGameLoginTicketsToRealm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Login tickets are valid for only five minutes. A v3 ticket has no authoritative
        // realm identity and must never be retroactively assigned to a realm.
        migrationBuilder.Sql("DELETE FROM `game_login_tickets`;", suppressTransaction: true);

        migrationBuilder.Sql(
            """
            ALTER TABLE `game_login_tickets`
                ADD COLUMN `realm_id` INT UNSIGNED NOT NULL AFTER `account_id`,
                ADD CONSTRAINT `CK_game_login_tickets_realm_id` CHECK (`realm_id` > 0);
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 4,
                `migration_id` = '20261013050000_BindGameLoginTicketsToRealm',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 3
              AND `migration_id` = '20261013010000_AddRealmRegistry';
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Reverting realm-bound tickets would permit unbound login authorization. Restore a verified backup after shutting down both servers, or review an explicit data-preserving migration.");
    }
}
