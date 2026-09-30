using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

public partial class AddSocialRelationPersistence : Migration
{
    private const string SchemaGuardTable = "openconquer_social_relation_schema_guard";
    private const string PreviousMigrationId = "20260927225127_AddItemLifetimePersistence";
    private const string CurrentMigrationId = "20260930033806_AddSocialRelationPersistence";
    private const string NormalizedOwnerCheckClause = "owner_character_id>=1000000";
    private const string NormalizedCounterpartCheckClause = "counterpart_character_id>=1000000";
    private const string NormalizedDistinctCharactersCheckClause = "owner_character_id<>counterpart_character_id";
    private const string NormalizedKindCheckClause = "kindin1,2";

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

            SET @openconquer_social_table_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
                  AND `TABLE_TYPE` = 'BASE TABLE'
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
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                        AND @openconquer_social_table_count IN (0, 1)
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 6
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                        AND @openconquer_social_table_count = 1
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
            CREATE TABLE IF NOT EXISTS `social_relations`
            (
                `owner_character_id` INT UNSIGNED NOT NULL,
                `counterpart_character_id` INT UNSIGNED NOT NULL,
                `kind` TINYINT UNSIGNED NOT NULL,

                CONSTRAINT `PK_social_relations`
                    PRIMARY KEY (`owner_character_id`, `kind`, `counterpart_character_id`),

                KEY `IX_social_relations_counterpart_kind_owner`
                    (`counterpart_character_id`, `kind`, `owner_character_id`),

                CONSTRAINT `FK_social_relations_characters_owner_character_id`
                    FOREIGN KEY (`owner_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `FK_social_relations_characters_counterpart_character_id`
                    FOREIGN KEY (`counterpart_character_id`)
                    REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT
                    ON UPDATE RESTRICT,

                CONSTRAINT `CK_social_relations_owner_character_id`
                    CHECK (`owner_character_id` >= 1000000),

                CONSTRAINT `CK_social_relations_counterpart_character_id`
                    CHECK (`counterpart_character_id` >= 1000000),

                CONSTRAINT `CK_social_relations_distinct_characters`
                    CHECK (`owner_character_id` <> `counterpart_character_id`),

                CONSTRAINT `CK_social_relations_kind`
                    CHECK (`kind` IN (1, 2))
            )
            CHARACTER SET utf8mb4
            COLLATE utf8mb4_0900_as_cs;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_social_table_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
                  AND `TABLE_TYPE` = 'BASE TABLE'
                  AND `TABLE_COLLATION` = 'utf8mb4_0900_as_cs'
            );

            SET @openconquer_social_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 3
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'owner_character_id'
                                AND `ORDINAL_POSITION` = 1
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            WHEN `COLUMN_NAME` = 'counterpart_character_id'
                                AND `ORDINAL_POSITION` = 2
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            WHEN `COLUMN_NAME` = 'kind'
                                AND `ORDINAL_POSITION` = 3
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            ELSE 0
                        END
                    ) = 3,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
            );

            SET @openconquer_social_indexes_valid =
            (
                SELECT IF(
                    COUNT(*) = 6
                    AND SUM(
                        CASE
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'kind' THEN 1
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'counterpart_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'counterpart_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'kind' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            ELSE 0
                        END
                    ) = 6,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
            );

            SET @openconquer_social_foreign_keys_valid =
            (
                SELECT IF(
                    COUNT(*) = 2
                    AND SUM(
                        CASE
                            WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_social_relations_characters_owner_character_id'
                                AND `kcu`.`COLUMN_NAME` = 'owner_character_id'
                                AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                                AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                                AND `rc`.`DELETE_RULE` = 'RESTRICT'
                                AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                            WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_social_relations_characters_counterpart_character_id'
                                AND `kcu`.`COLUMN_NAME` = 'counterpart_character_id'
                                AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                                AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                                AND `rc`.`DELETE_RULE` = 'RESTRICT'
                                AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                            ELSE 0
                        END
                    ) = 2,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
                INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                    ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                    AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
                WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `kcu`.`TABLE_NAME` = 'social_relations'
                  AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            );

            SET @openconquer_social_checks_valid =
            (
                SELECT IF(
                    COUNT(*) = 4
                    AND SUM(
                        CASE
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_owner_character_id'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedOwnerCheckClause}' THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_counterpart_character_id'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedCounterpartCheckClause}' THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_distinct_characters'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') IN
                                (
                                    '{NormalizedDistinctCharactersCheckClause}',
                                    'owner_character_id!=counterpart_character_id'
                                ) THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_kind'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedKindCheckClause}' THEN 1
                            ELSE 0
                        END
                    ) = 4,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
                INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
                    ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA`
                    AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
                WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `tc`.`TABLE_NAME` = 'social_relations'
                  AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
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
                    @openconquer_social_table_valid = 1
                    AND @openconquer_social_columns_valid = 1
                    AND @openconquer_social_indexes_valid = 1
                    AND @openconquer_social_foreign_keys_valid = 1
                    AND @openconquer_social_checks_valid = 1,
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
                `schema_version` = 6,
                `migration_id` = '{CurrentMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 5
                      AND `migration_id` = '{PreviousMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 6
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
                    @openconquer_game_schema_version = 6
                    AND @openconquer_game_migration_id = '{CurrentMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_social_table_count = NULL;
            SET @openconquer_social_table_valid = NULL;
            SET @openconquer_social_columns_valid = NULL;
            SET @openconquer_social_indexes_valid = NULL;
            SET @openconquer_social_foreign_keys_valid = NULL;
            SET @openconquer_social_checks_valid = NULL;
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

            SET @openconquer_social_table_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
                  AND `TABLE_TYPE` = 'BASE TABLE'
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
                        @openconquer_game_schema_version = 6
                        AND @openconquer_game_migration_id = '{CurrentMigrationId}'
                        AND @openconquer_social_table_count IN (0, 1)
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 5
                        AND @openconquer_game_migration_id = '{PreviousMigrationId}'
                        AND @openconquer_social_table_count = 0
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
            SET @openconquer_social_table_valid =
            (
                SELECT IF(COUNT(*) = 1, 1, 0)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
                  AND `TABLE_TYPE` = 'BASE TABLE'
                  AND `TABLE_COLLATION` = 'utf8mb4_0900_as_cs'
            );

            SET @openconquer_social_columns_valid =
            (
                SELECT IF(
                    COUNT(*) = 3
                    AND SUM(
                        CASE
                            WHEN `COLUMN_NAME` = 'owner_character_id'
                                AND `ORDINAL_POSITION` = 1
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            WHEN `COLUMN_NAME` = 'counterpart_character_id'
                                AND `ORDINAL_POSITION` = 2
                                AND `COLUMN_TYPE` = 'int unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            WHEN `COLUMN_NAME` = 'kind'
                                AND `ORDINAL_POSITION` = 3
                                AND `COLUMN_TYPE` = 'tinyint unsigned'
                                AND `IS_NULLABLE` = 'NO'
                                AND `COLUMN_DEFAULT` IS NULL
                                AND `EXTRA` = '' THEN 1
                            ELSE 0
                        END
                    ) = 3,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
            );

            SET @openconquer_social_indexes_valid =
            (
                SELECT IF(
                    COUNT(*) = 6
                    AND SUM(
                        CASE
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'kind' THEN 1
                            WHEN `INDEX_NAME` = 'PRIMARY'
                                AND `NON_UNIQUE` = 0
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'counterpart_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 1
                                AND `COLUMN_NAME` = 'counterpart_character_id' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 2
                                AND `COLUMN_NAME` = 'kind' THEN 1
                            WHEN `INDEX_NAME` = 'IX_social_relations_counterpart_kind_owner'
                                AND `NON_UNIQUE` = 1
                                AND `SEQ_IN_INDEX` = 3
                                AND `COLUMN_NAME` = 'owner_character_id' THEN 1
                            ELSE 0
                        END
                    ) = 6,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`STATISTICS`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
            );

            SET @openconquer_social_foreign_keys_valid =
            (
                SELECT IF(
                    COUNT(*) = 2
                    AND SUM(
                        CASE
                            WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_social_relations_characters_owner_character_id'
                                AND `kcu`.`COLUMN_NAME` = 'owner_character_id'
                                AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                                AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                                AND `rc`.`DELETE_RULE` = 'RESTRICT'
                                AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                            WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_social_relations_characters_counterpart_character_id'
                                AND `kcu`.`COLUMN_NAME` = 'counterpart_character_id'
                                AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                                AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                                AND `rc`.`DELETE_RULE` = 'RESTRICT'
                                AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                            ELSE 0
                        END
                    ) = 2,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
                INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                    ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA`
                    AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
                WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `kcu`.`TABLE_NAME` = 'social_relations'
                  AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
            );

            SET @openconquer_social_checks_valid =
            (
                SELECT IF(
                    COUNT(*) = 4
                    AND SUM(
                        CASE
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_owner_character_id'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedOwnerCheckClause}' THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_counterpart_character_id'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedCounterpartCheckClause}' THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_distinct_characters'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') IN
                                (
                                    '{NormalizedDistinctCharactersCheckClause}',
                                    'owner_character_id!=counterpart_character_id'
                                ) THEN 1
                            WHEN `tc`.`CONSTRAINT_NAME` = 'CK_social_relations_kind'
                                AND `tc`.`ENFORCED` = 'YES'
                                AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = '{NormalizedKindCheckClause}' THEN 1
                            ELSE 0
                        END
                    ) = 4,
                    1,
                    0
                )
                FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
                INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
                    ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA`
                    AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
                WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE()
                  AND `tc`.`TABLE_NAME` = 'social_relations'
                  AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
            );

            SET @openconquer_social_row_count = 0;

            SET @openconquer_sql =
                IF(
                    @openconquer_social_table_count = 1,
                    'SELECT COUNT(*) INTO @openconquer_social_row_count FROM `social_relations`',
                    'SET @openconquer_social_row_count = 0'
                );

            PREPARE openconquer_statement FROM @openconquer_sql;
            EXECUTE openconquer_statement;
            DEALLOCATE PREPARE openconquer_statement;

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
                        @openconquer_game_schema_version = 6
                        AND
                        (
                            @openconquer_social_table_count = 0
                            OR
                            (
                                @openconquer_social_table_count = 1
                                AND @openconquer_social_table_valid = 1
                                AND @openconquer_social_columns_valid = 1
                                AND @openconquer_social_indexes_valid = 1
                                AND @openconquer_social_foreign_keys_valid = 1
                                AND @openconquer_social_checks_valid = 1
                                AND @openconquer_social_row_count = 0
                            )
                        )
                    )
                    OR
                    (
                        @openconquer_game_schema_version = 5
                        AND @openconquer_social_table_count = 0
                    ),
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_sql = NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS `social_relations`;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            $"""
            SET @openconquer_social_table_count =
            (
                SELECT COUNT(*)
                FROM `INFORMATION_SCHEMA`.`TABLES`
                WHERE `TABLE_SCHEMA` = DATABASE()
                  AND `TABLE_NAME` = 'social_relations'
            );

            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;

            CREATE TEMPORARY TABLE `{SchemaGuardTable}`
            (
                `is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1)
            );

            INSERT INTO `{SchemaGuardTable}` (`is_valid`)
            VALUES
            (
                IF(@openconquer_social_table_count = 0, 1, 0)
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            UPDATE `schema_compatibility`
            SET
                `schema_version` = 5,
                `migration_id` = '{PreviousMigrationId}',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND
              (
                  (
                      `schema_version` = 6
                      AND `migration_id` = '{CurrentMigrationId}'
                  )
                  OR
                  (
                      `schema_version` = 5
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
                    @openconquer_game_schema_version = 5
                    AND @openconquer_game_migration_id = '{PreviousMigrationId}',
                    1,
                    0
                )
            );

            DROP TEMPORARY TABLE `{SchemaGuardTable}`;

            SET @openconquer_game_schema_version = NULL;
            SET @openconquer_game_migration_id = NULL;
            SET @openconquer_social_table_count = NULL;
            SET @openconquer_social_table_valid = NULL;
            SET @openconquer_social_columns_valid = NULL;
            SET @openconquer_social_indexes_valid = NULL;
            SET @openconquer_social_foreign_keys_valid = NULL;
            SET @openconquer_social_checks_valid = NULL;
            SET @openconquer_social_row_count = NULL;
            """,
            suppressTransaction: true);
    }
}
