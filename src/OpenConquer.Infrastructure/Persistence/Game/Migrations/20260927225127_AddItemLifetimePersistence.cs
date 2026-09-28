using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

public partial class AddItemLifetimePersistence : Migration
{
    private const string SchemaGuardTable = "openconquer_item_lifetime_schema_guard";
    private const string PreviousMigrationId = "20260927121811_AddItemPersistenceFoundation";
    private const string CurrentMigrationId = "20260927225127_AddItemLifetimePersistence";

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
                        @openconquer_game_schema_version = 4
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 5
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
            SET @openconquer_items_table_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `TABLE_TYPE` = 'BASE TABLE'
                  AND `TABLE_COLLATION` = 'utf8mb4_0900_as_cs'
            );

            SET @openconquer_items_base_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 26
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'item_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = 'auto_increment' THEN 1
                            WHEN `COLUMN_NAME` = 'owner_character_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_type_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'location_kind'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_set'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_slot'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'maximum_durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_a'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket_progress_or_steed_color_or_monster_counter_baseline'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket1_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket2_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'hidden_attack_effect'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_b'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'addition_level'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'damage_reduction_percent_or_steed_composition_red'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_binding_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'enchantment_life_bonus_or_steed_composition_green'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'monster_restraint_id_or_steed_composition_blue'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'is_suspicious'
                                AND `COLUMN_TYPE` = 'tinyint(1)'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_lock_state_mask'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_unlock_at_utc'
                                AND `COLUMN_TYPE` = 'datetime(6)'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_color'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'composition_progress'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'inscribed_syndicate_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'stack_quantity'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            ELSE 0
                        END
                    ) = 26,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` NOT IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_lifetime_columns_valid =
            (
                SELECT IF(
                    COUNT(*) BETWEEN 0 AND 3
                    AND COALESCE(
                        SUM(
                            CASE
                                WHEN `COLUMN_NAME` = 'lifetime_state'
                                    AND `COLUMN_TYPE` = 'tinyint unsigned'
                                    AND `IS_NULLABLE` = 'NO'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                WHEN `COLUMN_NAME` = 'lifetime_duration_seconds'
                                    AND `COLUMN_TYPE` = 'int'
                                    AND `IS_NULLABLE` = 'YES'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                WHEN `COLUMN_NAME` = 'lifetime_expires_at_utc'
                                    AND `COLUMN_TYPE` = 'datetime(6)'
                                    AND `IS_NULLABLE` = 'YES'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) = COUNT(*),
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_items_base_indexes_valid =
            (
                SELECT IF(
                    COUNT(*) = 6
                    AND SUM(
                        CASE
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'item_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'location_kind' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'equipment_set' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'equipment_slot' THEN 1
                            ELSE 0
                        END
                    ) = 6,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` IN
                  (
                      'PRIMARY',
                      'IX_items_owner_character_id_location_kind',
                      'UX_items_owner_equipment_position'
                  )
            );

            SET @openconquer_lifetime_index_valid =
            (
                SELECT IF(
                    COUNT(*) = 0
                    OR
                    (
                        COUNT(*) = 2
                        AND SUM(
                            CASE
                                WHEN `NON_UNIQUE` = 1
                                    AND `SEQ_IN_INDEX` = 1
                                    AND `COLUMN_NAME` = 'lifetime_state' THEN 1
                                WHEN `NON_UNIQUE` = 1
                                    AND `SEQ_IN_INDEX` = 2
                                    AND `COLUMN_NAME` = 'lifetime_expires_at_utc' THEN 1
                                ELSE 0
                            END
                        ) = 2
                    ),
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            );

            SET @openconquer_items_foreign_key_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
                INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                    ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                    AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
                WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `kcu`.`TABLE_NAME` = 'items'
                  AND `kcu`.`CONSTRAINT_NAME` = 'FK_items_characters_owner_character_id'
                  AND `kcu`.`COLUMN_NAME` = 'owner_character_id'
                  AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                  AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                  AND `rc`.`DELETE_RULE` = 'RESTRICT'
                  AND `rc`.`UPDATE_RULE` = 'RESTRICT'
            );

            SET @openconquer_items_base_checks_valid =
            (
                SELECT IF(COUNT(*) = 9, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` IN
                  (
                      'CK_items_item_type_id',
                      'CK_items_location_kind',
                      'CK_items_location_payload',
                      'CK_items_equipment_set',
                      'CK_items_equipment_slot',
                      'CK_items_alternate_equipment_slot',
                      'CK_items_is_suspicious',
                      'CK_items_equipment_unlock_schedule',
                      'CK_items_stack_quantity'
                  )
            );

            SET @openconquer_lifetime_check_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
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
                    @openconquer_items_table_valid = 1
                    AND @openconquer_items_base_columns_valid = 1
                    AND @openconquer_lifetime_columns_valid = 1
                    AND @openconquer_items_base_indexes_valid = 1
                    AND @openconquer_lifetime_index_valid = 1
                    AND @openconquer_items_foreign_key_valid = 1
                    AND @openconquer_items_base_checks_valid = 1
                    AND @openconquer_lifetime_check_count IN (0, 1),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_items_row_count =
            (
                SELECT COUNT(*)
                FROM `items`
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
                    @openconquer_game_schema_version = 5
                    OR
                    (
                        @openconquer_game_schema_version = 4
                        AND @openconquer_items_row_count = 0
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
            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 4
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_state'
                    ),
                    'ALTER TABLE `items` ADD COLUMN `lifetime_state` TINYINT UNSIGNED NOT NULL AFTER `stack_quantity`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 4
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_duration_seconds'
                    ),
                    'ALTER TABLE `items` ADD COLUMN `lifetime_duration_seconds` INT NULL AFTER `lifetime_state`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 4
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_expires_at_utc'
                    ),
                    'ALTER TABLE `items` ADD COLUMN `lifetime_expires_at_utc` DATETIME(6) NULL AFTER `lifetime_duration_seconds`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 4
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`STATISTICS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
                    ),
                    'CREATE INDEX `IX_items_lifetime_state_expires_at_utc` ON `items` (`lifetime_state`, `lifetime_expires_at_utc`)',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 4
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                        WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
                    ),
                    'ALTER TABLE `items` ADD CONSTRAINT `CK_items_lifetime` CHECK ((`lifetime_state` = 1 AND `lifetime_duration_seconds` IS NULL AND `lifetime_expires_at_utc` IS NULL) OR (`lifetime_state` = 2 AND `lifetime_duration_seconds` IS NOT NULL AND `lifetime_duration_seconds` > 0 AND `lifetime_expires_at_utc` IS NULL) OR (`lifetime_state` = 3 AND `lifetime_duration_seconds` IS NULL AND `lifetime_expires_at_utc` IS NOT NULL))',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql = NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_lifetime_columns_present =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_lifetime_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 3
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'lifetime_state'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'lifetime_duration_seconds'
                                AND `COLUMN_TYPE` = 'int'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'lifetime_expires_at_utc'
                                AND `COLUMN_TYPE` = 'datetime(6)'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            ELSE 0
                        END
                    ) = 3,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_lifetime_index_rows =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            );

            SET @openconquer_lifetime_index_valid =
            (
                SELECT IF(
                    COUNT(*) = 2
                    AND SUM(
                        CASE
                            WHEN `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'lifetime_state' THEN 1
                            WHEN `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'lifetime_expires_at_utc' THEN 1
                            ELSE 0
                        END
                    ) = 2,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            );

            SET @openconquer_lifetime_check_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
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
                    @openconquer_items_table_valid = 1
                    AND @openconquer_items_base_columns_valid = 1
                    AND @openconquer_lifetime_columns_present = 3
                    AND @openconquer_lifetime_columns_valid = 1
                    AND @openconquer_items_base_indexes_valid = 1
                    AND @openconquer_lifetime_index_rows = 2
                    AND @openconquer_lifetime_index_valid = 1
                    AND @openconquer_items_foreign_key_valid = 1
                    AND @openconquer_items_base_checks_valid = 1
                    AND @openconquer_lifetime_check_count = 1,
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
                `schema_version` = 5,
                `migration_id` = '{CurrentMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 4
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 5
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
                    @openconquer_game_schema_version = 5
                    AND @openconquer_game_migration_id = '{CurrentMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_items_table_valid = NULL;
            SET @openconquer_items_base_columns_valid = NULL;
            SET @openconquer_lifetime_columns_present = NULL;
            SET @openconquer_lifetime_columns_valid = NULL;
            SET @openconquer_items_base_indexes_valid = NULL;
            SET @openconquer_lifetime_index_rows = NULL;
            SET @openconquer_lifetime_index_valid = NULL;
            SET @openconquer_items_foreign_key_valid = NULL;
            SET @openconquer_items_base_checks_valid = NULL;
            SET @openconquer_lifetime_check_count = NULL;
            SET @openconquer_items_row_count = NULL;
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
                        @openconquer_game_schema_version = 5
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 4
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
            SET @openconquer_items_table_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `TABLE_TYPE` = 'BASE TABLE'
                  AND `TABLE_COLLATION` = 'utf8mb4_0900_as_cs'
            );

            SET @openconquer_items_base_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 26
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'item_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = 'auto_increment' THEN 1
                            WHEN `COLUMN_NAME` = 'owner_character_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_type_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'location_kind'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_set'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_slot'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'maximum_durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_a'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket_progress_or_steed_color_or_monster_counter_baseline'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket1_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket2_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'hidden_attack_effect'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_b'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'addition_level'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'damage_reduction_percent_or_steed_composition_red'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_binding_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'enchantment_life_bonus_or_steed_composition_green'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'monster_restraint_id_or_steed_composition_blue'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'is_suspicious'
                                AND `COLUMN_TYPE` = 'tinyint(1)'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_lock_state_mask'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_unlock_at_utc'
                                AND `COLUMN_TYPE` = 'datetime(6)'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_color'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'composition_progress'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'inscribed_syndicate_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'stack_quantity'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            ELSE 0
                        END
                    ) = 26,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` NOT IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_lifetime_columns_valid =
            (
                SELECT IF(
                    COUNT(*) BETWEEN 0 AND 3
                    AND COALESCE(
                        SUM(
                            CASE
                                WHEN `COLUMN_NAME` = 'lifetime_state'
                                    AND `COLUMN_TYPE` = 'tinyint unsigned'
                                    AND `IS_NULLABLE` = 'NO'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                WHEN `COLUMN_NAME` = 'lifetime_duration_seconds'
                                    AND `COLUMN_TYPE` = 'int'
                                    AND `IS_NULLABLE` = 'YES'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                WHEN `COLUMN_NAME` = 'lifetime_expires_at_utc'
                                    AND `COLUMN_TYPE` = 'datetime(6)'
                                    AND `IS_NULLABLE` = 'YES'
                                    AND `COLUMN_DEFAULT` IS NULL THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) = COUNT(*),
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_items_base_indexes_valid =
            (
                SELECT IF(
                    COUNT(*) = 6
                    AND SUM(
                        CASE
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'item_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'location_kind' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'equipment_set' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'equipment_slot' THEN 1
                            ELSE 0
                        END
                    ) = 6,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` IN
                  (
                      'PRIMARY',
                      'IX_items_owner_character_id_location_kind',
                      'UX_items_owner_equipment_position'
                  )
            );

            SET @openconquer_lifetime_index_valid =
            (
                SELECT IF(
                    COUNT(*) = 0
                    OR
                    (
                        COUNT(*) = 2
                        AND SUM(
                            CASE
                                WHEN `NON_UNIQUE` = 1
                                    AND `SEQ_IN_INDEX` = 1
                                    AND `COLUMN_NAME` = 'lifetime_state' THEN 1
                                WHEN `NON_UNIQUE` = 1
                                    AND `SEQ_IN_INDEX` = 2
                                    AND `COLUMN_NAME` = 'lifetime_expires_at_utc' THEN 1
                                ELSE 0
                            END
                        ) = 2
                    ),
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            );

            SET @openconquer_items_foreign_key_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
                INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                    ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                    AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
                WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `kcu`.`TABLE_NAME` = 'items'
                  AND `kcu`.`CONSTRAINT_NAME` = 'FK_items_characters_owner_character_id'
                  AND `kcu`.`COLUMN_NAME` = 'owner_character_id'
                  AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                  AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                  AND `rc`.`DELETE_RULE` = 'RESTRICT'
                  AND `rc`.`UPDATE_RULE` = 'RESTRICT'
            );

            SET @openconquer_items_base_checks_valid =
            (
                SELECT IF(COUNT(*) = 9, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` IN
                  (
                      'CK_items_item_type_id',
                      'CK_items_location_kind',
                      'CK_items_location_payload',
                      'CK_items_equipment_set',
                      'CK_items_equipment_slot',
                      'CK_items_alternate_equipment_slot',
                      'CK_items_is_suspicious',
                      'CK_items_equipment_unlock_schedule',
                      'CK_items_stack_quantity'
                  )
            );

            SET @openconquer_lifetime_check_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
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
                    @openconquer_items_table_valid = 1
                    AND @openconquer_items_base_columns_valid = 1
                    AND @openconquer_lifetime_columns_valid = 1
                    AND @openconquer_items_base_indexes_valid = 1
                    AND @openconquer_lifetime_index_valid = 1
                    AND @openconquer_items_foreign_key_valid = 1
                    AND @openconquer_items_base_checks_valid = 1
                    AND @openconquer_lifetime_check_count IN (0, 1),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_items_row_count =
            (
                SELECT COUNT(*)
                FROM `items`
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
                    @openconquer_game_schema_version = 4
                    OR
                    (
                        @openconquer_game_schema_version = 5
                        AND @openconquer_items_row_count = 0
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
            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 5
                    AND EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                        WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
                    ),
                    'ALTER TABLE `items` DROP CHECK `CK_items_lifetime`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 5
                    AND EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`STATISTICS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
                    ),
                    'DROP INDEX `IX_items_lifetime_state_expires_at_utc` ON `items`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 5
                    AND EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_expires_at_utc'
                    ),
                    'ALTER TABLE `items` DROP COLUMN `lifetime_expires_at_utc`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 5
                    AND EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_duration_seconds'
                    ),
                    'ALTER TABLE `items` DROP COLUMN `lifetime_duration_seconds`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql =
                IF(
                    @openconquer_game_schema_version = 5
                    AND EXISTS
                    (
                        SELECT 1
                        FROM `INFORMATION_SCHEMA`.`COLUMNS`
                        WHERE `TABLE_SCHEMA` = DATABASE()
                          AND `TABLE_NAME` = 'items'
                          AND `COLUMN_NAME` = 'lifetime_state'
                    ),
                    'ALTER TABLE `items` DROP COLUMN `lifetime_state`',
                    'DO 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

            SET @openconquer_sql = NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_items_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 26
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'item_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = 'auto_increment' THEN 1
                            WHEN `COLUMN_NAME` = 'owner_character_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_type_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'location_kind'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_set'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_slot'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'maximum_durability'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_a'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket_progress_or_steed_color_or_monster_counter_baseline'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket1_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'socket2_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'hidden_attack_effect'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'retail_compatibility_byte_b'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'addition_level'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'damage_reduction_percent_or_steed_composition_red'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'item_binding_code'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'enchantment_life_bonus_or_steed_composition_green'
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'monster_restraint_id_or_steed_composition_blue'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'is_suspicious'
                                AND `COLUMN_TYPE` = 'tinyint(1)'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_lock_state_mask'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_unlock_at_utc'
                                AND `COLUMN_TYPE` = 'datetime(6)'
                                AND `IS_NULLABLE` = 'YES'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'equipment_color'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'composition_progress'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'inscribed_syndicate_id'
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            WHEN `COLUMN_NAME` = 'stack_quantity'
                                AND `COLUMN_TYPE` = 'smallint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL THEN 1
                            ELSE 0
                        END
                    ) = 26,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
            );

            SET @openconquer_items_indexes_valid =
            (
                SELECT IF(
                    COUNT(*) = 6
                    AND SUM(
                        CASE
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'item_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_items_owner_character_id_location_kind'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'location_kind' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'equipment_set' THEN 1
                            WHEN `INDEX_NAME` = 'UX_items_owner_equipment_position'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'equipment_slot' THEN 1
                            ELSE 0
                        END
                    ) = 6,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` IN
                  (
                      'PRIMARY',
                      'IX_items_owner_character_id_location_kind',
                      'UX_items_owner_equipment_position'
                  )
            );

            SET @openconquer_items_foreign_key_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
                INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                    ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                    AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
                WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `kcu`.`TABLE_NAME` = 'items'
                  AND `kcu`.`CONSTRAINT_NAME` = 'FK_items_characters_owner_character_id'
                  AND `kcu`.`COLUMN_NAME` = 'owner_character_id'
                  AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                  AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                  AND `rc`.`DELETE_RULE` = 'RESTRICT'
                  AND `rc`.`UPDATE_RULE` = 'RESTRICT'
            );

            SET @openconquer_items_checks_valid =
            (
                SELECT IF(COUNT(*) = 9, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_TYPE` = 'CHECK'
                  AND `ENFORCED` = 'YES'
                  AND `CONSTRAINT_NAME` IN
                  (
                      'CK_items_item_type_id',
                      'CK_items_location_kind',
                      'CK_items_location_payload',
                      'CK_items_equipment_set',
                      'CK_items_equipment_slot',
                      'CK_items_alternate_equipment_slot',
                      'CK_items_is_suspicious',
                      'CK_items_equipment_unlock_schedule',
                      'CK_items_stack_quantity'
                  )
            );

            SET @openconquer_lifetime_columns_present =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `COLUMN_NAME` IN
                  (
                      'lifetime_state',
                      'lifetime_duration_seconds',
                      'lifetime_expires_at_utc'
                  )
            );

            SET @openconquer_lifetime_index_rows =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `INDEX_NAME` = 'IX_items_lifetime_state_expires_at_utc'
            );

            SET @openconquer_lifetime_check_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
                WHERE `CONSTRAINT_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
                  AND `CONSTRAINT_NAME` = 'CK_items_lifetime'
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
                    @openconquer_items_table_valid = 1
                    AND @openconquer_items_columns_valid = 1
                    AND @openconquer_items_indexes_valid = 1
                    AND @openconquer_items_foreign_key_valid = 1
                    AND @openconquer_items_checks_valid = 1
                    AND @openconquer_lifetime_columns_present = 0
                    AND @openconquer_lifetime_index_rows = 0
                    AND @openconquer_lifetime_check_count = 0,
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            UPDATE `schema_compatibility`
            SET
                `schema_version` = 4,
                `migration_id` = '{PreviousMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 5
                      AND `migration_id` = '{CurrentMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 4
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
                    @openconquer_game_schema_version = 4
                    AND @openconquer_game_migration_id = '{PreviousMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_items_table_valid = NULL;
            SET @openconquer_items_base_columns_valid = NULL;
            SET @openconquer_lifetime_columns_valid = NULL;
            SET @openconquer_items_base_indexes_valid = NULL;
            SET @openconquer_lifetime_index_valid = NULL;
            SET @openconquer_items_foreign_key_valid = NULL;
            SET @openconquer_items_base_checks_valid = NULL;
            SET @openconquer_items_row_count = NULL;
            SET @openconquer_items_columns_valid = NULL;
            SET @openconquer_items_indexes_valid = NULL;
            SET @openconquer_items_checks_valid = NULL;
            SET @openconquer_lifetime_columns_present = NULL;
            SET @openconquer_lifetime_index_rows = NULL;
            SET @openconquer_lifetime_check_count = NULL;
            """,
            suppressTransaction: true);
    }
}
