using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

public partial class AddSyndicatePersistence : Migration
{
    private const string SchemaGuardTable = "openconquer_syndicate_schema_guard";
    private const string PreviousMigrationId = "20261004193352_AddMagicPersistence";
    private const string CurrentMigrationId = "20261008033456_AddSyndicatePersistence";

    private const string InspectStructureSql = """
        SET @openconquer_syn_exists =
        (
            SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates' AND `TABLE_TYPE` = 'BASE TABLE'
        );
        SET @openconquer_syn_members_exists =
        (
            SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships' AND `TABLE_TYPE` = 'BASE TABLE'
        );
        SET @openconquer_syn_table_valid =
        (
            SELECT IF(COUNT(*) = 1 AND SUM(`TABLE_COLLATION` = 'utf8mb4_0900_as_cs') = 1, 1, 0)
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates' AND `TABLE_TYPE` = 'BASE TABLE'
        );
        SET @openconquer_syn_columns_valid =
        (
            SELECT IF(COUNT(*) = 8 AND SUM(
                CASE
                    WHEN `COLUMN_NAME` = 'syndicate_id' AND `ORDINAL_POSITION` = 1 AND `COLUMN_TYPE` = 'smallint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = 'auto_increment' THEN 1
                    WHEN `COLUMN_NAME` = 'name' AND `ORDINAL_POSITION` = 2 AND `COLUMN_TYPE` = 'varchar(16)'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = ''
                        AND `CHARACTER_SET_NAME` = 'utf8mb4' AND `COLLATION_NAME` = 'utf8mb4_bin' THEN 1
                    WHEN `COLUMN_NAME` = 'leader_character_id' AND `ORDINAL_POSITION` = 3 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'silver_fund' AND `ORDINAL_POSITION` = 4 AND `COLUMN_TYPE` = 'bigint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'emoney_fund' AND `ORDINAL_POSITION` = 5 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'required_level' AND `ORDINAL_POSITION` = 6 AND `COLUMN_TYPE` = 'tinyint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'required_profession' AND `ORDINAL_POSITION` = 7 AND `COLUMN_TYPE` = 'tinyint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'required_metempsychosis' AND `ORDINAL_POSITION` = 8 AND `COLUMN_TYPE` = 'tinyint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    ELSE 0
                END) = 8, 1, 0)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates'
        );
        SET @openconquer_syn_indexes_valid =
        (
            SELECT IF(COUNT(*) = 3 AND SUM(
                CASE
                    WHEN `INDEX_NAME` = 'PRIMARY' AND `NON_UNIQUE` = 0 AND `SEQ_IN_INDEX` = 1
                        AND `COLUMN_NAME` = 'syndicate_id' THEN 1
                    WHEN `INDEX_NAME` = 'UX_syndicates_name' AND `NON_UNIQUE` = 0 AND `SEQ_IN_INDEX` = 1
                        AND `COLUMN_NAME` = 'name' THEN 1
                    WHEN `INDEX_NAME` = 'UX_syndicates_leader_character_id' AND `NON_UNIQUE` = 0 AND `SEQ_IN_INDEX` = 1
                        AND `COLUMN_NAME` = 'leader_character_id' THEN 1
                    ELSE 0
                END) = 3, 1, 0)
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicates'
        );
        SET @openconquer_syn_foreign_keys_valid =
        (
            SELECT IF(COUNT(*) = 1 AND SUM(
                `kcu`.`CONSTRAINT_NAME` = 'FK_syndicates_characters_leader_character_id'
                AND `kcu`.`COLUMN_NAME` = 'leader_character_id'
                AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                AND `rc`.`DELETE_RULE` = 'RESTRICT' AND `rc`.`UPDATE_RULE` = 'RESTRICT') = 1, 1, 0)
            FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
            INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA` AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
            WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE() AND `kcu`.`TABLE_NAME` = 'syndicates'
                AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
        );
        SET @openconquer_syn_checks_valid =
        (
            SELECT IF(COUNT(*) = 2 AND SUM(
                CASE
                    WHEN `tc`.`CONSTRAINT_NAME` = 'CK_syndicates_name_length' AND `tc`.`ENFORCED` = 'YES'
                        AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = 'char_lengthnamebetween1and16' THEN 1
                    WHEN `tc`.`CONSTRAINT_NAME` = 'CK_syndicates_leader_character_id' AND `tc`.`ENFORCED` = 'YES'
                        AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = 'leader_character_id>=1000000' THEN 1
                    ELSE 0
                END) = 2, 1, 0)
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
            INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
                ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA` AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
            WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE() AND `tc`.`TABLE_NAME` = 'syndicates'
                AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
        );
        SET @openconquer_syn_members_table_valid =
        (
            SELECT IF(COUNT(*) = 1 AND SUM(`TABLE_COLLATION` = 'utf8mb4_0900_as_cs') = 1, 1, 0)
            FROM `INFORMATION_SCHEMA`.`TABLES`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships' AND `TABLE_TYPE` = 'BASE TABLE'
        );
        SET @openconquer_syn_members_columns_valid =
        (
            SELECT IF(COUNT(*) = 6 AND SUM(
                CASE
                    WHEN `COLUMN_NAME` = 'character_id' AND `ORDINAL_POSITION` = 1 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'syndicate_id' AND `ORDINAL_POSITION` = 2 AND `COLUMN_TYPE` = 'smallint unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'rank' AND `ORDINAL_POSITION` = 3 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'proffer' AND `ORDINAL_POSITION` = 4 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'position_expiration_unix_seconds' AND `ORDINAL_POSITION` = 5 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    WHEN `COLUMN_NAME` = 'join_date_unix_seconds' AND `ORDINAL_POSITION` = 6 AND `COLUMN_TYPE` = 'int unsigned'
                        AND `IS_NULLABLE` = 'NO' AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = '' THEN 1
                    ELSE 0
                END) = 6, 1, 0)
            FROM `INFORMATION_SCHEMA`.`COLUMNS`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships'
        );
        SET @openconquer_syn_members_indexes_valid =
        (
            SELECT IF(COUNT(*) = 3 AND SUM(
                CASE
                    WHEN `INDEX_NAME` = 'PRIMARY' AND `NON_UNIQUE` = 0 AND `SEQ_IN_INDEX` = 1
                        AND `COLUMN_NAME` = 'character_id' THEN 1
                    WHEN `INDEX_NAME` = 'IX_syndicate_memberships_syndicate_id_character_id'
                        AND `NON_UNIQUE` = 1 AND `SEQ_IN_INDEX` = 1 AND `COLUMN_NAME` = 'syndicate_id' THEN 1
                    WHEN `INDEX_NAME` = 'IX_syndicate_memberships_syndicate_id_character_id'
                        AND `NON_UNIQUE` = 1 AND `SEQ_IN_INDEX` = 2 AND `COLUMN_NAME` = 'character_id' THEN 1
                    ELSE 0
                END) = 3, 1, 0)
            FROM `INFORMATION_SCHEMA`.`STATISTICS`
            WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'syndicate_memberships'
        );
        SET @openconquer_syn_members_foreign_keys_valid =
        (
            SELECT IF(COUNT(*) = 2 AND SUM(
                CASE
                    WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_syndicate_memberships_characters_character_id'
                        AND `kcu`.`COLUMN_NAME` = 'character_id' AND `kcu`.`REFERENCED_TABLE_NAME` = 'characters'
                        AND `kcu`.`REFERENCED_COLUMN_NAME` = 'character_id'
                        AND `rc`.`DELETE_RULE` = 'RESTRICT' AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                    WHEN `kcu`.`CONSTRAINT_NAME` = 'FK_syndicate_memberships_syndicates_syndicate_id'
                        AND `kcu`.`COLUMN_NAME` = 'syndicate_id' AND `kcu`.`REFERENCED_TABLE_NAME` = 'syndicates'
                        AND `kcu`.`REFERENCED_COLUMN_NAME` = 'syndicate_id'
                        AND `rc`.`DELETE_RULE` = 'RESTRICT' AND `rc`.`UPDATE_RULE` = 'RESTRICT' THEN 1
                    ELSE 0
                END) = 2, 1, 0)
            FROM `INFORMATION_SCHEMA`.`KEY_COLUMN_USAGE` AS `kcu`
            INNER JOIN `INFORMATION_SCHEMA`.`REFERENTIAL_CONSTRAINTS` AS `rc`
                ON `rc`.`CONSTRAINT_SCHEMA` = `kcu`.`CONSTRAINT_SCHEMA` AND `rc`.`CONSTRAINT_NAME` = `kcu`.`CONSTRAINT_NAME`
            WHERE `kcu`.`CONSTRAINT_SCHEMA` = DATABASE() AND `kcu`.`TABLE_NAME` = 'syndicate_memberships'
                AND `kcu`.`REFERENCED_TABLE_NAME` IS NOT NULL
        );
        SET @openconquer_syn_members_checks_valid =
        (
            SELECT IF(COUNT(*) = 2 AND SUM(
                CASE
                    WHEN `tc`.`CONSTRAINT_NAME` = 'CK_syndicate_memberships_character_id' AND `tc`.`ENFORCED` = 'YES'
                        AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = 'character_id>=1000000' THEN 1
                    WHEN `tc`.`CONSTRAINT_NAME` = 'CK_syndicate_memberships_syndicate_id' AND `tc`.`ENFORCED` = 'YES'
                        AND REGEXP_REPLACE(LOWER(`cc`.`CHECK_CLAUSE`), '[[:space:]`()]+', '') = 'syndicate_id>0' THEN 1
                    ELSE 0
                END) = 2, 1, 0)
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS` AS `tc`
            INNER JOIN `INFORMATION_SCHEMA`.`CHECK_CONSTRAINTS` AS `cc`
                ON `cc`.`CONSTRAINT_SCHEMA` = `tc`.`CONSTRAINT_SCHEMA` AND `cc`.`CONSTRAINT_NAME` = `tc`.`CONSTRAINT_NAME`
            WHERE `tc`.`CONSTRAINT_SCHEMA` = DATABASE() AND `tc`.`TABLE_NAME` = 'syndicate_memberships'
                AND `tc`.`CONSTRAINT_TYPE` = 'CHECK'
        );
        SET @openconquer_syn_valid = IF(
            @openconquer_syn_table_valid = 1 AND @openconquer_syn_columns_valid = 1
            AND @openconquer_syn_indexes_valid = 1 AND @openconquer_syn_foreign_keys_valid = 1
            AND @openconquer_syn_checks_valid = 1, 1, 0);
        SET @openconquer_syn_members_valid = IF(
            @openconquer_syn_members_table_valid = 1 AND @openconquer_syn_members_columns_valid = 1
            AND @openconquer_syn_members_indexes_valid = 1 AND @openconquer_syn_members_foreign_keys_valid = 1
            AND @openconquer_syn_members_checks_valid = 1, 1, 0);
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            SET @openconquer_syn_schema_version =
                (SELECT `schema_version` FROM `schema_compatibility` WHERE `component_name` = 'game');
            SET @openconquer_syn_migration_id =
                (SELECT `migration_id` FROM `schema_compatibility` WHERE `component_name` = 'game');
            {InspectStructureSql}
            {GuardSql("""
                (
                    @openconquer_syn_schema_version = 8
                    AND @openconquer_syn_migration_id = '20261004193352_AddMagicPersistence'
                    AND (@openconquer_syn_exists = 0 OR @openconquer_syn_valid = 1)
                    AND (@openconquer_syn_members_exists = 0 OR @openconquer_syn_members_valid = 1)
                    AND (@openconquer_syn_members_exists = 0 OR @openconquer_syn_exists = 1)
                )
                OR
                (
                    @openconquer_syn_schema_version = 9
                    AND @openconquer_syn_migration_id = '20261008033456_AddSyndicatePersistence'
                    AND @openconquer_syn_valid = 1 AND @openconquer_syn_members_valid = 1
                )
                """)}
            """, suppressTransaction: true);

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS `syndicates`
            (
                `syndicate_id` SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
                `name` VARCHAR(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
                `leader_character_id` INT UNSIGNED NOT NULL,
                `silver_fund` BIGINT UNSIGNED NOT NULL,
                `emoney_fund` INT UNSIGNED NOT NULL,
                `required_level` TINYINT UNSIGNED NOT NULL,
                `required_profession` TINYINT UNSIGNED NOT NULL,
                `required_metempsychosis` TINYINT UNSIGNED NOT NULL,

                CONSTRAINT `PK_syndicates` PRIMARY KEY (`syndicate_id`),
                UNIQUE INDEX `UX_syndicates_name` (`name`),
                UNIQUE INDEX `UX_syndicates_leader_character_id` (`leader_character_id`),
                CONSTRAINT `FK_syndicates_characters_leader_character_id`
                    FOREIGN KEY (`leader_character_id`) REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT ON UPDATE RESTRICT,
                CONSTRAINT `CK_syndicates_name_length` CHECK (CHAR_LENGTH(`name`) BETWEEN 1 AND 16),
                CONSTRAINT `CK_syndicates_leader_character_id` CHECK (`leader_character_id` >= 1000000)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;
            """, suppressTransaction: true);

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS `syndicate_memberships`
            (
                `character_id` INT UNSIGNED NOT NULL,
                `syndicate_id` SMALLINT UNSIGNED NOT NULL,
                `rank` INT UNSIGNED NOT NULL,
                `proffer` INT UNSIGNED NOT NULL,
                `position_expiration_unix_seconds` INT UNSIGNED NOT NULL,
                `join_date_unix_seconds` INT UNSIGNED NOT NULL,

                CONSTRAINT `PK_syndicate_memberships` PRIMARY KEY (`character_id`),
                INDEX `IX_syndicate_memberships_syndicate_id_character_id` (`syndicate_id`, `character_id`),
                CONSTRAINT `FK_syndicate_memberships_characters_character_id`
                    FOREIGN KEY (`character_id`) REFERENCES `characters` (`character_id`)
                    ON DELETE RESTRICT ON UPDATE RESTRICT,
                CONSTRAINT `FK_syndicate_memberships_syndicates_syndicate_id`
                    FOREIGN KEY (`syndicate_id`) REFERENCES `syndicates` (`syndicate_id`)
                    ON DELETE RESTRICT ON UPDATE RESTRICT,
                CONSTRAINT `CK_syndicate_memberships_character_id` CHECK (`character_id` >= 1000000),
                CONSTRAINT `CK_syndicate_memberships_syndicate_id` CHECK (`syndicate_id` > 0)
            ) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_as_cs;
            """, suppressTransaction: true);

        migrationBuilder.Sql($"""
            {InspectStructureSql}
            {GuardSql("@openconquer_syn_valid = 1 AND @openconquer_syn_members_valid = 1")}
            UPDATE `schema_compatibility`
            SET `schema_version` = 9, `migration_id` = '{CurrentMigrationId}', `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game' AND
            ((`schema_version` = 8 AND `migration_id` = '{PreviousMigrationId}')
             OR (`schema_version` = 9 AND `migration_id` = '{CurrentMigrationId}'));
            SET @openconquer_syn_schema_version =
                (SELECT `schema_version` FROM `schema_compatibility` WHERE `component_name` = 'game');
            SET @openconquer_syn_migration_id =
                (SELECT `migration_id` FROM `schema_compatibility` WHERE `component_name` = 'game');
            {GuardSql($"@openconquer_syn_schema_version = 9 AND @openconquer_syn_migration_id = '{CurrentMigrationId}'")}
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            SET @openconquer_syn_schema_version =
                (SELECT `schema_version` FROM `schema_compatibility` WHERE `component_name` = 'game');
            SET @openconquer_syn_migration_id =
                (SELECT `migration_id` FROM `schema_compatibility` WHERE `component_name` = 'game');
            {InspectStructureSql}
            {GuardSql($"""
                (
                    @openconquer_syn_schema_version = 9
                    AND @openconquer_syn_migration_id = '{CurrentMigrationId}'
                    AND (@openconquer_syn_exists = 0 OR @openconquer_syn_valid = 1)
                    AND (@openconquer_syn_members_exists = 0 OR @openconquer_syn_members_valid = 1)
                    AND (@openconquer_syn_members_exists = 0 OR @openconquer_syn_exists = 1)
                )
                OR
                (
                    @openconquer_syn_schema_version = 8
                    AND @openconquer_syn_migration_id = '{PreviousMigrationId}'
                    AND @openconquer_syn_exists = 0 AND @openconquer_syn_members_exists = 0
                )
                """)}
            SET @openconquer_syn_rows = 0;
            SET @openconquer_syn_members_rows = 0;
            SET @openconquer_syn_count_sql = IF(@openconquer_syn_exists = 1,
                'SELECT COUNT(*) INTO @openconquer_syn_rows FROM `syndicates`', 'SET @openconquer_syn_rows = 0');
            PREPARE openconquer_syn_count FROM @openconquer_syn_count_sql;
            EXECUTE openconquer_syn_count;
            DEALLOCATE PREPARE openconquer_syn_count;
            SET @openconquer_syn_members_count_sql = IF(@openconquer_syn_members_exists = 1,
                'SELECT COUNT(*) INTO @openconquer_syn_members_rows FROM `syndicate_memberships`',
                'SET @openconquer_syn_members_rows = 0');
            PREPARE openconquer_syn_members_count FROM @openconquer_syn_members_count_sql;
            EXECUTE openconquer_syn_members_count;
            DEALLOCATE PREPARE openconquer_syn_members_count;
            {GuardSql("@openconquer_syn_rows = 0 AND @openconquer_syn_members_rows = 0")}
            """, suppressTransaction: true);

        migrationBuilder.Sql("DROP TABLE IF EXISTS `syndicate_memberships`;", suppressTransaction: true);
        migrationBuilder.Sql("DROP TABLE IF EXISTS `syndicates`;", suppressTransaction: true);

        migrationBuilder.Sql($"""
            {InspectStructureSql}
            {GuardSql("@openconquer_syn_exists = 0 AND @openconquer_syn_members_exists = 0")}
            UPDATE `schema_compatibility`
            SET `schema_version` = 8, `migration_id` = '{PreviousMigrationId}', `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game' AND
            ((`schema_version` = 9 AND `migration_id` = '{CurrentMigrationId}')
             OR (`schema_version` = 8 AND `migration_id` = '{PreviousMigrationId}'));
            SET @openconquer_syn_schema_version =
                (SELECT `schema_version` FROM `schema_compatibility` WHERE `component_name` = 'game');
            SET @openconquer_syn_migration_id =
                (SELECT `migration_id` FROM `schema_compatibility` WHERE `component_name` = 'game');
            {GuardSql($"@openconquer_syn_schema_version = 8 AND @openconquer_syn_migration_id = '{PreviousMigrationId}'")}
            """, suppressTransaction: true);
    }

    private static string GuardSql(string condition)
    {
        return $"""
            DROP TEMPORARY TABLE IF EXISTS `{SchemaGuardTable}`;
            CREATE TEMPORARY TABLE `{SchemaGuardTable}` (`is_valid` TINYINT NOT NULL CHECK (`is_valid` = 1));
            INSERT INTO `{SchemaGuardTable}` (`is_valid`) VALUES (IF({condition}, 1, 0));
            DROP TEMPORARY TABLE `{SchemaGuardTable}`;
            """;
    }
}
