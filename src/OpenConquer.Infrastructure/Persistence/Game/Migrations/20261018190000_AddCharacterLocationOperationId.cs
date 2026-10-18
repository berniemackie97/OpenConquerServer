using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Game.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

[DbContext(typeof(GameDbContext))]
[Migration("20261018190000_AddCharacterLocationOperationId")]
public sealed class AddCharacterLocationOperationId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Requires an offline migration with all writers to characters stopped.
        migrationBuilder.Sql(
            """
            ALTER TABLE `characters`
                ADD COLUMN `last_location_operation_id` BINARY(16) NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 12,
                `migration_id` = '20261018190000_AddCharacterLocationOperationId',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND `schema_version` = 11
              AND `migration_id` = '20261018040000_AddCharacterLocationRevision';
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Downgrading committed location-operation evidence is unsafe for populated worlds.");
    }
}
