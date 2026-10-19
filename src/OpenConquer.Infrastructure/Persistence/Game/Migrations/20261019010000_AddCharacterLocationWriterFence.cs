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
        // Offline deployment only. Every pre-v13 location writer must be stopped first.
        migrationBuilder.Sql(
            """
            ALTER TABLE `characters`
                ADD COLUMN `location_writer_token` BINARY(16) NULL;
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
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Removing durable location writer generations would reauthorize stale writes.");
    }
}
