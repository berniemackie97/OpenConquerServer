using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20261013010000_AddRealmRegistry")]
public sealed class AddRealmRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE `realms`
            (
                `realm_id` INT UNSIGNED NOT NULL,
                `protocol_server_name` VARCHAR(15) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                `public_game_address` BINARY(4) NOT NULL,
                `public_game_port` SMALLINT UNSIGNED NOT NULL,
                `availability` TINYINT UNSIGNED NOT NULL,
                CONSTRAINT `PK_realms` PRIMARY KEY (`realm_id`),
                CONSTRAINT `UX_realms_protocol_server_name` UNIQUE (`protocol_server_name`),
                CONSTRAINT `CK_realms_id` CHECK (`realm_id` > 0),
                CONSTRAINT `CK_realms_protocol_server_name` CHECK (
                    CHAR_LENGTH(`protocol_server_name`) BETWEEN 1 AND 15
                    AND NOT REGEXP_LIKE(`protocol_server_name`, '[^A-Za-z0-9_()-]', 'c')),
                CONSTRAINT `CK_realms_public_game_address` CHECK (HEX(`public_game_address`) NOT IN ('00000000', 'FFFFFFFF')),
                CONSTRAINT `CK_realms_public_game_port` CHECK (`public_game_port` > 0),
                CONSTRAINT `CK_realms_availability` CHECK (`availability` IN (1, 2, 3))
            ) ENGINE=InnoDB DEFAULT CHARSET=ascii COLLATE=ascii_bin;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 3,
                `migration_id` = '20261013010000_AddRealmRegistry',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts'
              AND `schema_version` = 2
              AND `migration_id` = '20260907004603_ProtectGameLoginTicketAuthenticationKey';
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Downgrading the realm registry requires an operator-reviewed, data-preserving migration. Restore a verified backup instead of silently dropping registered realms.");
    }
}
