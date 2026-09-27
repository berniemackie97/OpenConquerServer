using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

public partial class AddItemPersistenceFoundation : Migration
{
    private const string SchemaGuardTable = "openconquer_item_persistence_schema_guard";
    private const string PreviousMigrationId = "20260923223920_RetainPreRebirthLevel";
    private const string CurrentMigrationId = "20260927121811_AddItemPersistenceFoundation";

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
                        @openconquer_game_schema_version = 3
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 4
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
            """
            CREATE TABLE IF NOT EXISTS `items`
            (
                `item_id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
                `owner_character_id` INT UNSIGNED NOT NULL,
                `item_type_id` INT UNSIGNED NOT NULL,
                `location_kind` TINYINT UNSIGNED NOT NULL,
                `equipment_set` TINYINT UNSIGNED NULL,
                `equipment_slot` TINYINT UNSIGNED NULL,
                `durability` SMALLINT UNSIGNED NOT NULL,
                `maximum_durability` SMALLINT UNSIGNED NOT NULL,
                `retail_compatibility_byte_a` TINYINT UNSIGNED NOT NULL,
                `socket_progress_or_steed_color_or_monster_counter_baseline` INT UNSIGNED NOT NULL,
                `socket1_code` TINYINT UNSIGNED NOT NULL,
                `socket2_code` TINYINT UNSIGNED NOT NULL,
                `hidden_attack_effect` INT UNSIGNED NOT NULL,
                `retail_compatibility_byte_b` TINYINT UNSIGNED NOT NULL,
                `addition_level` TINYINT UNSIGNED NOT NULL,
                `damage_reduction_percent_or_steed_composition_red` TINYINT UNSIGNED NOT NULL,
                `item_binding_code` TINYINT UNSIGNED NOT NULL,
                `enchantment_life_bonus_or_steed_composition_green` TINYINT UNSIGNED NOT NULL,
                `monster_restraint_id_or_steed_composition_blue` INT UNSIGNED NOT NULL,
                `is_suspicious` TINYINT(1) NOT NULL,
                `equipment_lock_state_mask` SMALLINT UNSIGNED NOT NULL,
                `equipment_unlock_at_utc` DATETIME(6) NULL,
                `equipment_color` SMALLINT UNSIGNED NOT NULL,
                `composition_progress` INT UNSIGNED NOT NULL,
                `inscribed_syndicate_id` INT UNSIGNED NOT NULL,
                `stack_quantity` SMALLINT UNSIGNED NOT NULL,

                CONSTRAINT `PK_items`
                    PRIMARY KEY (`item_id`),

                CONSTRAINT `FK_items_characters_owner_character_id`
                    FOREIGN KEY (`owner_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `CK_items_item_type_id`
                    CHECK (`item_type_id` > 0),

                CONSTRAINT `CK_items_location_kind`
                    CHECK (`location_kind` IN (1, 2)),

                CONSTRAINT `CK_items_location_payload`
                    CHECK (
                        (`location_kind` = 1 AND `equipment_set` IS NULL AND `equipment_slot` IS NULL)
                        OR
                        (`location_kind` = 2 AND `equipment_set` IS NOT NULL AND `equipment_slot` IS NOT NULL)
                    ),

                CONSTRAINT `CK_items_equipment_set`
                    CHECK (`equipment_set` IS NULL OR `equipment_set` IN (1, 2)),

                CONSTRAINT `CK_items_equipment_slot`
                    CHECK (`equipment_slot` IS NULL OR `equipment_slot` BETWEEN 1 AND 16),

                CONSTRAINT `CK_items_alternate_equipment_slot`
                    CHECK (
                        `equipment_set` IS NULL
                        OR `equipment_set` <> 2
                        OR `equipment_slot` BETWEEN 1 AND 9
                    ),

                CONSTRAINT `CK_items_is_suspicious`
                    CHECK (`is_suspicious` IN (0, 1)),

                CONSTRAINT `CK_items_equipment_unlock_schedule`
                    CHECK (
                        ((`equipment_lock_state_mask` & 2) = 0 AND `equipment_unlock_at_utc` IS NULL)
                        OR
                        ((`equipment_lock_state_mask` & 2) = 2 AND `equipment_unlock_at_utc` IS NOT NULL)
                    ),

                CONSTRAINT `CK_items_stack_quantity`
                    CHECK (`stack_quantity` >= 1),

                KEY `IX_items_owner_character_id_location_kind`
                    (`owner_character_id`, `location_kind`),

                UNIQUE KEY `UX_items_owner_equipment_position`
                    (`owner_character_id`, `equipment_set`, `equipment_slot`)
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs;
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
                    AND @openconquer_items_checks_valid = 1,
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
                `schema_version` = 4,
                `migration_id` = '{CurrentMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 3
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 4
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
                    @openconquer_game_schema_version = 4
                    AND @openconquer_game_migration_id = '{CurrentMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_items_table_valid = NULL;
            SET @openconquer_items_columns_valid = NULL;
            SET @openconquer_items_indexes_valid = NULL;
            SET @openconquer_items_foreign_key_valid = NULL;
            SET @openconquer_items_checks_valid = NULL;
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
                        @openconquer_game_schema_version = 4
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 3
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
            """
            DROP TABLE IF EXISTS `items`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            UPDATE `schema_compatibility`
            SET
                `schema_version` = 3,
                `migration_id` = '{PreviousMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 4
                      AND `migration_id` = '{CurrentMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 3
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
              );

            SET @openconquer_items_table_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'items'
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
                    @openconquer_items_table_count = 0
                    AND @openconquer_game_schema_version = 3
                    AND @openconquer_game_migration_id = '{PreviousMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_items_table_count = NULL;
            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            """,
            suppressTransaction: true);
    }
}
