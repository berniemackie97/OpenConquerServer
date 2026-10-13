using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Game.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

[DbContext(typeof(GameDbContext))]
[Migration("20261013030000_AddGameRealmBinding")]
public sealed class AddGameRealmBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE `realm_binding`
            (
                `binding_key` TINYINT UNSIGNED NOT NULL,
                `realm_id` INT UNSIGNED NOT NULL,
                CONSTRAINT `PK_realm_binding` PRIMARY KEY (`binding_key`),
                CONSTRAINT `CK_realm_binding_key` CHECK (`binding_key` = 1),
                CONSTRAINT `CK_realm_binding_id` CHECK (`realm_id` > 0)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_as_cs;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 10,
                `migration_id` = '20261013030000_AddGameRealmBinding',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND `schema_version` = 9
              AND `migration_id` = '20261008033456_AddSyndicatePersistence';
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Downgrading the game-realm binding requires an independently reviewed migration. Never silently remove a realm identity from a populated Game database.");
    }
}
