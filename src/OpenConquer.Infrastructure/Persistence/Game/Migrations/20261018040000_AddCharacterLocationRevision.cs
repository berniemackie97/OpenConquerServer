using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenConquer.Infrastructure.Persistence.Game.Context;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

[DbContext(typeof(GameDbContext))]
[Migration("20261018040000_AddCharacterLocationRevision")]
public sealed class AddCharacterLocationRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Only an offline migration with all character writers stopped may change this schema.
        migrationBuilder.Sql(
            """
            ALTER TABLE `characters`
                ADD COLUMN `location_revision` BIGINT NOT NULL DEFAULT 0,
                ADD CONSTRAINT `CK_characters_location_revision` CHECK (`location_revision` >= 0);
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            UPDATE `schema_compatibility`
            SET `schema_version` = 11,
                `migration_id` = '20261018040000_AddCharacterLocationRevision',
                `applied_at_utc` = UTC_TIMESTAMP(6)
            WHERE `component_name` = 'game'
              AND `schema_version` = 10
              AND `migration_id` = '20261013030000_AddGameRealmBinding';
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Downgrading monotonic character-location revisions is unsafe for populated worlds.");
    }
}
