using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

public partial class RetainPreRebirthLevel : Migration
{
    private const string SchemaGuardTable = "openconquer_pre_rebirth_level_schema_guard";
    private const string PreRebirthLevelConstraintName = "CK_characters_pre_rebirth_level";
    private const string RebirthStateConstraintName = "CK_characters_rebirth_state";
    private const string PreviousMigrationId = "20260922025854_UseSignedCharacterPkPoints";
    private const string CurrentMigrationId = "20260923223920_RetainPreRebirthLevel";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            SET @openconquer_game_schema_version =
            (
                SELECT `schema_version`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            SET @openconquer_game_migration_id =
            (
                SELECT `migration_id`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    (
                        @openconquer_game_schema_version = 2
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 3
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                    ),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_rebirth_count_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_pre_rebirth_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_default =
            (
                SELECT `COLUMN_DEFAULT`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_extra =
            (
                SELECT `EXTRA`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{PreRebirthLevelConstraintName}'
            );

            SET @openconquer_rebirth_state_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{RebirthStateConstraintName}'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_rebirth_count_column_count = 1
                    AND @openconquer_rebirth_count_column_type = 'tinyint unsigned'
                    AND @openconquer_rebirth_count_is_nullable = 'NO'
                    AND
                    (
                        (
                            @openconquer_pre_rebirth_column_count = 0
                            AND @openconquer_pre_rebirth_constraint_count = 0
                            AND @openconquer_rebirth_state_constraint_count = 0
                        )
                        OR
                        (
                            @openconquer_pre_rebirth_column_count = 1
                            AND @openconquer_pre_rebirth_column_type = 'tinyint unsigned'
                            AND @openconquer_pre_rebirth_is_nullable IN ('YES', 'NO')
                            AND @openconquer_pre_rebirth_column_default IS NULL
                            AND @openconquer_pre_rebirth_extra = ''
                            AND @openconquer_pre_rebirth_constraint_count IN (0, 1)
                            AND @openconquer_rebirth_state_constraint_count IN (0, 1)
                        )
                    ),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            SET @openconquer_pre_rebirth_add_column =
                IF(
                    @openconquer_pre_rebirth_column_count = 0,
                    'ALTER TABLE `characters`
                        ADD COLUMN `pre_rebirth_level` TINYINT UNSIGNED NULL',
                    'DO 0'
                );

            PREPARE openconquer_pre_rebirth_add_column_statement
                FROM @openconquer_pre_rebirth_add_column;

            EXECUTE openconquer_pre_rebirth_add_column_statement;

            DEALLOCATE PREPARE openconquer_pre_rebirth_add_column_statement;

            UPDATE `characters`
            SET `pre_rebirth_level` = 0
            WHERE `pre_rebirth_level` IS NULL
              AND `rebirth_count` = 0;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            SELECT IF(COUNT(*) = 0, 1, 0)
            FROM `characters`
            WHERE `pre_rebirth_level` IS NULL
               OR `pre_rebirth_level` > 140
               OR (`rebirth_count` = 0 AND `pre_rebirth_level` <> 0)
               OR (`rebirth_count` > 0 AND `pre_rebirth_level` = 0);

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_pre_rebirth_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_make_required =
                IF(
                    @openconquer_pre_rebirth_is_nullable = 'YES',
                    'ALTER TABLE `characters`
                        MODIFY COLUMN `pre_rebirth_level` TINYINT UNSIGNED NOT NULL',
                    'DO 0'
                );

            PREPARE openconquer_pre_rebirth_make_required_statement
                FROM @openconquer_pre_rebirth_make_required;

            EXECUTE openconquer_pre_rebirth_make_required_statement;

            DEALLOCATE PREPARE openconquer_pre_rebirth_make_required_statement;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_rebirth_state_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{RebirthStateConstraintName}'
            );

            SET @openconquer_pre_rebirth_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{PreRebirthLevelConstraintName}'
            );

            SET @openconquer_drop_rebirth_state_constraint =
                IF(
                    @openconquer_rebirth_state_constraint_count = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{RebirthStateConstraintName}`',
                    'DO 0'
                );

            PREPARE openconquer_drop_rebirth_state_constraint_statement
                FROM @openconquer_drop_rebirth_state_constraint;

            EXECUTE openconquer_drop_rebirth_state_constraint_statement;

            DEALLOCATE PREPARE openconquer_drop_rebirth_state_constraint_statement;

            SET @openconquer_drop_pre_rebirth_constraint =
                IF(
                    @openconquer_pre_rebirth_constraint_count = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{PreRebirthLevelConstraintName}`',
                    'DO 0'
                );

            PREPARE openconquer_drop_pre_rebirth_constraint_statement
                FROM @openconquer_drop_pre_rebirth_constraint;

            EXECUTE openconquer_drop_pre_rebirth_constraint_statement;

            DEALLOCATE PREPARE openconquer_drop_pre_rebirth_constraint_statement;

            ALTER TABLE `characters`
                ADD CONSTRAINT `{PreRebirthLevelConstraintName}`
                    CHECK (`pre_rebirth_level` BETWEEN 0 AND 140),
                ADD CONSTRAINT `{RebirthStateConstraintName}`
                    CHECK (
                        (`rebirth_count` = 0 AND `pre_rebirth_level` = 0)
                        OR (`rebirth_count` > 0 AND `pre_rebirth_level` > 0)
                    );
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_rebirth_count_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_pre_rebirth_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_default =
            (
                SELECT `COLUMN_DEFAULT`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_extra =
            (
                SELECT `EXTRA`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{PreRebirthLevelConstraintName}'
                  AND `ENFORCED` = 'YES'
            );

            SET @openconquer_rebirth_state_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{RebirthStateConstraintName}'
                  AND `ENFORCED` = 'YES'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_rebirth_count_column_count = 1
                    AND @openconquer_rebirth_count_column_type = 'tinyint unsigned'
                    AND @openconquer_rebirth_count_is_nullable = 'NO'
                    AND @openconquer_pre_rebirth_column_count = 1
                    AND @openconquer_pre_rebirth_column_type = 'tinyint unsigned'
                    AND @openconquer_pre_rebirth_is_nullable = 'NO'
                    AND @openconquer_pre_rebirth_column_default IS NULL
                    AND @openconquer_pre_rebirth_extra = ''
                    AND @openconquer_pre_rebirth_constraint_count = 1
                    AND @openconquer_rebirth_state_constraint_count = 1,
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 3,
                `migration_id` = '{CurrentMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 2
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 3
                      AND `migration_id` = '{CurrentMigrationId}'
                  )
              );

            SET @openconquer_game_schema_version =
            (
                SELECT `schema_version`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            SET @openconquer_game_migration_id =
            (
                SELECT `migration_id`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_game_schema_version = 3
                    AND @openconquer_game_migration_id = '{CurrentMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_rebirth_count_column_count = NULL;
            SET @openconquer_rebirth_count_column_type = NULL;
            SET @openconquer_rebirth_count_is_nullable = NULL;
            SET @openconquer_pre_rebirth_column_count = NULL;
            SET @openconquer_pre_rebirth_column_type = NULL;
            SET @openconquer_pre_rebirth_is_nullable = NULL;
            SET @openconquer_pre_rebirth_column_default = NULL;
            SET @openconquer_pre_rebirth_extra = NULL;
            SET @openconquer_pre_rebirth_constraint_count = NULL;
            SET @openconquer_rebirth_state_constraint_count = NULL;
            SET @openconquer_pre_rebirth_add_column = NULL;
            SET @openconquer_pre_rebirth_make_required = NULL;
            SET @openconquer_drop_rebirth_state_constraint = NULL;
            SET @openconquer_drop_pre_rebirth_constraint = NULL;
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            SET @openconquer_game_schema_version =
            (
                SELECT `schema_version`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            SET @openconquer_game_migration_id =
            (
                SELECT `migration_id`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    (
                        @openconquer_game_schema_version = 3
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 2
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                    ),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_rebirth_count_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_rebirth_count_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'rebirth_count'
            );

            SET @openconquer_pre_rebirth_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_type =
            (
                SELECT `COLUMN_TYPE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_is_nullable =
            (
                SELECT `IS_NULLABLE`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_column_default =
            (
                SELECT `COLUMN_DEFAULT`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_extra =
            (
                SELECT `EXTRA`
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{PreRebirthLevelConstraintName}'
            );

            SET @openconquer_rebirth_state_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{RebirthStateConstraintName}'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_rebirth_count_column_count = 1
                    AND @openconquer_rebirth_count_column_type = 'tinyint unsigned'
                    AND @openconquer_rebirth_count_is_nullable = 'NO'
                    AND
                    (
                        (
                            @openconquer_pre_rebirth_column_count = 0
                            AND @openconquer_pre_rebirth_constraint_count = 0
                            AND @openconquer_rebirth_state_constraint_count = 0
                        )
                        OR
                        (
                            @openconquer_pre_rebirth_column_count = 1
                            AND @openconquer_pre_rebirth_column_type = 'tinyint unsigned'
                            AND @openconquer_pre_rebirth_is_nullable = 'NO'
                            AND @openconquer_pre_rebirth_column_default IS NULL
                            AND @openconquer_pre_rebirth_extra = ''
                            AND @openconquer_pre_rebirth_constraint_count IN (0, 1)
                            AND @openconquer_rebirth_state_constraint_count IN (0, 1)
                        )
                    ),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_drop_rebirth_state_constraint =
                IF(
                    @openconquer_rebirth_state_constraint_count = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{RebirthStateConstraintName}`',
                    'DO 0'
                );

            PREPARE openconquer_drop_rebirth_state_constraint_statement
                FROM @openconquer_drop_rebirth_state_constraint;

            EXECUTE openconquer_drop_rebirth_state_constraint_statement;

            DEALLOCATE PREPARE openconquer_drop_rebirth_state_constraint_statement;

            SET @openconquer_drop_pre_rebirth_constraint =
                IF(
                    @openconquer_pre_rebirth_constraint_count = 1,
                    'ALTER TABLE `characters`
                        DROP CHECK `{PreRebirthLevelConstraintName}`',
                    'DO 0'
                );

            PREPARE openconquer_drop_pre_rebirth_constraint_statement
                FROM @openconquer_drop_pre_rebirth_constraint;

            EXECUTE openconquer_drop_pre_rebirth_constraint_statement;

            DEALLOCATE PREPARE openconquer_drop_pre_rebirth_constraint_statement;

            SET @openconquer_pre_rebirth_drop_column =
                IF(
                    @openconquer_pre_rebirth_column_count = 1,
                    'ALTER TABLE `characters`
                        DROP COLUMN `pre_rebirth_level`',
                    'DO 0'
                );

            PREPARE openconquer_pre_rebirth_drop_column_statement
                FROM @openconquer_pre_rebirth_drop_column;

            EXECUTE openconquer_pre_rebirth_drop_column_statement;

            DEALLOCATE PREPARE openconquer_pre_rebirth_drop_column_statement;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_pre_rebirth_column_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'pre_rebirth_level'
            );

            SET @openconquer_pre_rebirth_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{PreRebirthLevelConstraintName}'
            );

            SET @openconquer_rebirth_state_constraint_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'characters'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `CONSTRAINT_NAME` = '{RebirthStateConstraintName}'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_pre_rebirth_column_count = 0
                    AND @openconquer_pre_rebirth_constraint_count = 0
                    AND @openconquer_rebirth_state_constraint_count = 0,
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 2,
                `migration_id` = '{PreviousMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 3
                      AND `migration_id` = '{CurrentMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 2
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
              );

            SET @openconquer_game_schema_version =
            (
                SELECT `schema_version`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            SET @openconquer_game_migration_id =
            (
                SELECT `migration_id`
                FROM `schema_compatibility`
                WHERE `component_name` = 'game'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(
                    @openconquer_game_schema_version = 2
                    AND @openconquer_game_migration_id = '{PreviousMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_rebirth_count_column_count = NULL;
            SET @openconquer_rebirth_count_column_type = NULL;
            SET @openconquer_rebirth_count_is_nullable = NULL;
            SET @openconquer_pre_rebirth_column_count = NULL;
            SET @openconquer_pre_rebirth_column_type = NULL;
            SET @openconquer_pre_rebirth_is_nullable = NULL;
            SET @openconquer_pre_rebirth_column_default = NULL;
            SET @openconquer_pre_rebirth_extra = NULL;
            SET @openconquer_pre_rebirth_constraint_count = NULL;
            SET @openconquer_rebirth_state_constraint_count = NULL;
            SET @openconquer_drop_rebirth_state_constraint = NULL;
            SET @openconquer_drop_pre_rebirth_constraint = NULL;
            SET @openconquer_pre_rebirth_drop_column = NULL;
            """,
            suppressTransaction: true);
    }
}
