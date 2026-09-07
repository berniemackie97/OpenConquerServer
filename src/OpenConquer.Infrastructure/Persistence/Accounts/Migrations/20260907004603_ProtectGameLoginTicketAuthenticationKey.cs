using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

/// <inheritdoc />
public partial class ProtectGameLoginTicketAuthenticationKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM `game_login_tickets`;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            SET @openconquer_has_legacy_authentication_key =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'game_login_tickets'
                    AND `COLUMN_NAME` = 'authentication_key'
            );

            SET @openconquer_game_login_ticket_upgrade =
                IF(
                    @openconquer_has_legacy_authentication_key = 1,
                    'ALTER TABLE `game_login_tickets`
                        DROP COLUMN `authentication_key`,
                        ADD COLUMN `authentication_key_verifier` BINARY(32) NOT NULL,
                        ADD COLUMN `authentication_key_verifier_key_id` SMALLINT UNSIGNED NOT NULL,
                        ADD CONSTRAINT `CK_game_login_tickets_session_uid`
                            CHECK (`session_uid` > 0),
                        ADD CONSTRAINT `CK_game_login_tickets_verifier_key_id`
                            CHECK (`authentication_key_verifier_key_id` > 0)',
                    'DO 0'
                );

            PREPARE openconquer_game_login_ticket_upgrade_statement
                FROM @openconquer_game_login_ticket_upgrade;

            EXECUTE openconquer_game_login_ticket_upgrade_statement;

            DEALLOCATE PREPARE openconquer_game_login_ticket_upgrade_statement;

            SET @openconquer_game_login_ticket_upgrade = NULL;
            SET @openconquer_has_legacy_authentication_key = NULL;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            SELECT
                `authentication_key_verifier`,
                `authentication_key_verifier_key_id`
            FROM `game_login_tickets`
            LIMIT 0;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 2,
                `migration_id` = '20260907004603_ProtectGameLoginTicketAuthenticationKey',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts';
            """,
            suppressTransaction: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM `game_login_tickets`;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            SET @openconquer_has_authentication_key_verifier =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'game_login_tickets'
                    AND `COLUMN_NAME` = 'authentication_key_verifier'
            );

            SET @openconquer_game_login_ticket_downgrade =
                IF(
                    @openconquer_has_authentication_key_verifier = 1,
                    'ALTER TABLE `game_login_tickets`
                        DROP CHECK `CK_game_login_tickets_session_uid`,
                        DROP CHECK `CK_game_login_tickets_verifier_key_id`,
                        DROP COLUMN `authentication_key_verifier`,
                        DROP COLUMN `authentication_key_verifier_key_id`,
                        ADD COLUMN `authentication_key` INT UNSIGNED NOT NULL',
                    'DO 0'
                );

            PREPARE openconquer_game_login_ticket_downgrade_statement
                FROM @openconquer_game_login_ticket_downgrade;

            EXECUTE openconquer_game_login_ticket_downgrade_statement;

            DEALLOCATE PREPARE openconquer_game_login_ticket_downgrade_statement;

            SET @openconquer_game_login_ticket_downgrade = NULL;
            SET @openconquer_has_authentication_key_verifier = NULL;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            SELECT `authentication_key`
            FROM `game_login_tickets`
            LIMIT 0;
            """,
            suppressTransaction: true
        );

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 1,
                `migration_id` = '20260906043138_InitialAccountSchema',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'accounts';
            """,
            suppressTransaction: true
        );
    }
}
