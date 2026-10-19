using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Game.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

[DbContext(typeof(GameDbContext))]
[Migration("20261019010000_AddCharacterLocationWriterFence")]
public sealed class AddCharacterLocationWriterFence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // MySQL DDL commits independently of the EF history insert. Only the exact
        // expected partial schema and predecessor/current compatibility markers may resume.
        // Every character writer must be stopped throughout this offline migration.
        migrationBuilder.Sql(
            """
            SET @oc_fence_contract_count =
            (
                SELECT COUNT(*) FROM `schema_compatibility`
                WHERE `component_name` = 'game'
                  AND ((`schema_version` = 12 AND
                        `migration_id` = '20261018190000_AddCharacterLocationOperationId')
                    OR (`schema_version` = 13 AND
                        `migration_id` = '20261019010000_AddCharacterLocationWriterFence'))
            );
            SET @oc_fence_contract_sql = IF(@oc_fence_contract_count = 1,
                'SELECT 1', 'SELECT * FROM `openconquer_invalid_writer_fence_compatibility`');
            PREPARE oc_fence_contract_guard FROM @oc_fence_contract_sql;
            EXECUTE oc_fence_contract_guard;
            DEALLOCATE PREPARE oc_fence_contract_guard;

            SET @oc_fence_column_count =
            (
                SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'location_writer_token'
            );
            SET @oc_fence_valid_column_count =
            (
                SELECT COUNT(*) FROM `INFORMATION_SCHEMA`.`COLUMNS`
                WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'characters'
                  AND `COLUMN_NAME` = 'location_writer_token'
                  AND `COLUMN_TYPE` = 'binary(16)' AND `IS_NULLABLE` = 'YES'
                  AND `COLUMN_DEFAULT` IS NULL AND `EXTRA` = ''
            );
            SET @oc_fence_schema_sql = CASE
                WHEN @oc_fence_column_count = 0 THEN
                    'ALTER TABLE `characters` ADD COLUMN `location_writer_token` BINARY(16) NULL'
                WHEN @oc_fence_valid_column_count = 1 THEN 'SELECT 1'
                ELSE 'SELECT * FROM `openconquer_invalid_location_writer_token_shape`'
            END;
            PREPARE oc_fence_schema_guard FROM @oc_fence_schema_sql;
            EXECUTE oc_fence_schema_guard;
            DEALLOCATE PREPARE oc_fence_schema_guard;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 13,
                `migration_id` = '20261019010000_AddCharacterLocationWriterFence',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND `schema_version` = 12
              AND `migration_id` = '20261018190000_AddCharacterLocationOperationId';

            SET @oc_fence_final_contract_count =
            (
                SELECT COUNT(*) FROM `schema_compatibility`
                WHERE `component_name` = 'game'
                  AND `schema_version` = 13
                  AND `migration_id` = '20261019010000_AddCharacterLocationWriterFence'
            );
            SET @oc_fence_final_contract_sql = IF(@oc_fence_final_contract_count = 1,
                'SELECT 1', 'SELECT * FROM `openconquer_invalid_final_writer_fence_contract`');
            PREPARE oc_fence_final_guard FROM @oc_fence_final_contract_sql;
            EXECUTE oc_fence_final_guard;
            DEALLOCATE PREPARE oc_fence_final_guard;
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Removing durable location writer generations would reauthorize stale writes.");
    }
}
