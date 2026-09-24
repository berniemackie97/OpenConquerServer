using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

/// <inheritdoc />
public partial class UseSignedCharacterPkPoints : Migration
{
    private const string SchemaGuardTable = "openconquer_pk_points_schema_guard";
    private const string SignedRangeGuard = "CK_characters_pk_points_signed_migration";
    private const string UnsignedRangeGuard = "CK_characters_pk_points_unsigned_migration";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            SET @openconquer_pk_points_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL
                    CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_pk_points_is_nullable = 'NO'
                    AND @openconquer_pk_points_column_type IN
                        ('smallint unsigned', 'smallint'),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_guard_exists =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE
                    `CONSTRAINT_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `CONSTRAINT_TYPE` = 'CHECK'
                    AND `CONSTRAINT_NAME` = '{SignedRangeGuard}'
            );

            SET @openconquer_pk_points_drop_guard =
                IF(
                    @openconquer_pk_points_column_type = 'smallint unsigned'
                    AND @openconquer_pk_points_guard_exists = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{SignedRangeGuard}`',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_drop_guard_statement
                FROM @openconquer_pk_points_drop_guard;

            EXECUTE openconquer_pk_points_drop_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_drop_guard_statement;

            SET @openconquer_pk_points_add_guard =
                IF(
                    @openconquer_pk_points_column_type = 'smallint unsigned',
                    'ALTER TABLE `characters`
                        ADD CONSTRAINT `{SignedRangeGuard}`
                        CHECK (`pk_points` <= 32767)',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_add_guard_statement
                FROM @openconquer_pk_points_add_guard;

            EXECUTE openconquer_pk_points_add_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_add_guard_statement;

            SET @openconquer_pk_points_upgrade =
                IF(
                    @openconquer_pk_points_column_type = 'smallint unsigned',
                    'ALTER TABLE `characters`
                        MODIFY COLUMN `pk_points` SMALLINT NOT NULL',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_upgrade_statement
                FROM @openconquer_pk_points_upgrade;

            EXECUTE openconquer_pk_points_upgrade_statement;

            DEALLOCATE PREPARE openconquer_pk_points_upgrade_statement;

            SET @openconquer_pk_points_guard_exists =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE
                    `CONSTRAINT_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `CONSTRAINT_TYPE` = 'CHECK'
                    AND `CONSTRAINT_NAME` = '{SignedRangeGuard}'
            );

            SET @openconquer_pk_points_drop_guard =
                IF(
                    @openconquer_pk_points_guard_exists = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{SignedRangeGuard}`',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_drop_guard_statement
                FROM @openconquer_pk_points_drop_guard;

            EXECUTE openconquer_pk_points_drop_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_drop_guard_statement;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            SET @openconquer_pk_points_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL
                    CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_pk_points_column_type = 'smallint'
                    AND @openconquer_pk_points_is_nullable = 'NO',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_pk_points_column_type = NULL;
            SET @openconquer_pk_points_is_nullable = NULL;
            SET @openconquer_pk_points_guard_exists = NULL;
            SET @openconquer_pk_points_add_guard = NULL;
            SET @openconquer_pk_points_drop_guard = NULL;
            SET @openconquer_pk_points_upgrade = NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 2,
                `migration_id` = '20260922025854_UseSignedCharacterPkPoints',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game';
            """,
            suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            SET @openconquer_pk_points_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL
                    CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_pk_points_is_nullable = 'NO'
                    AND @openconquer_pk_points_column_type IN
                        ('smallint', 'smallint unsigned'),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_guard_exists =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE
                    `CONSTRAINT_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `CONSTRAINT_TYPE` = 'CHECK'
                    AND `CONSTRAINT_NAME` = '{UnsignedRangeGuard}'
            );

            SET @openconquer_pk_points_drop_guard =
                IF(
                    @openconquer_pk_points_column_type = 'smallint'
                    AND @openconquer_pk_points_guard_exists = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{UnsignedRangeGuard}`',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_drop_guard_statement
                FROM @openconquer_pk_points_drop_guard;

            EXECUTE openconquer_pk_points_drop_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_drop_guard_statement;

            SET @openconquer_pk_points_add_guard =
                IF(
                    @openconquer_pk_points_column_type = 'smallint',
                    'ALTER TABLE `characters`
                        ADD CONSTRAINT `{UnsignedRangeGuard}`
                        CHECK (`pk_points` >= 0)',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_add_guard_statement
                FROM @openconquer_pk_points_add_guard;

            EXECUTE openconquer_pk_points_add_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_add_guard_statement;

            SET @openconquer_pk_points_downgrade =
                IF(
                    @openconquer_pk_points_column_type = 'smallint',
                    'ALTER TABLE `characters`
                        MODIFY COLUMN `pk_points` SMALLINT UNSIGNED NOT NULL',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_downgrade_statement
                FROM @openconquer_pk_points_downgrade;

            EXECUTE openconquer_pk_points_downgrade_statement;

            DEALLOCATE PREPARE openconquer_pk_points_downgrade_statement;

            SET @openconquer_pk_points_guard_exists =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE
                    `CONSTRAINT_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `CONSTRAINT_TYPE` = 'CHECK'
                    AND `CONSTRAINT_NAME` = '{UnsignedRangeGuard}'
            );

            SET @openconquer_pk_points_drop_guard =
                IF(
                    @openconquer_pk_points_guard_exists = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{UnsignedRangeGuard}`',
                    'DO 0'
                );

            PREPARE openconquer_pk_points_drop_guard_statement
                FROM @openconquer_pk_points_drop_guard;

            EXECUTE openconquer_pk_points_drop_guard_statement;

            DEALLOCATE PREPARE openconquer_pk_points_drop_guard_statement;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_pk_points_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            SET @openconquer_pk_points_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE
                    `TABLE_SCHEMA` = DATABASE()
                    AND `TABLE_NAME` = 'characters'
                    AND `COLUMN_NAME` = 'pk_points'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL
                    CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_pk_points_column_type = 'smallint unsigned'
                    AND @openconquer_pk_points_is_nullable = 'NO',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_pk_points_column_type = NULL;
            SET @openconquer_pk_points_is_nullable = NULL;
            SET @openconquer_pk_points_guard_exists = NULL;
            SET @openconquer_pk_points_add_guard = NULL;
            SET @openconquer_pk_points_drop_guard = NULL;
            SET @openconquer_pk_points_downgrade = NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 1,
                `migration_id` = '20260914210346_InitialGameSchema',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game';
            """,
            suppressTransaction: true);
    }
}
