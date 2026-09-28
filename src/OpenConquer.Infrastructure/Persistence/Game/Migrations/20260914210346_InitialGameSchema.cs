using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

/// <inheritdoc />
public partial class InitialGameSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "characters",
            columns: table => new
            {
                character_id = table.Column<uint>(type: "int unsigned", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                name = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false, collation: "utf8mb4_bin")
                    .Annotation("MySql:CharSet", "utf8mb4"),
                appearance_composite = table.Column<uint>(type: "int unsigned", nullable: false),
                hair_composite = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                experience = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                strength = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                agility = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                vitality = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                spirit = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                unspent_attribute_points = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                current_life = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                current_mana = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                profession = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                first_profession = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                previous_profession = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                rebirth_count = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                silver = table.Column<uint>(type: "int unsigned", nullable: false),
                conquer_points = table.Column<uint>(type: "int unsigned", nullable: false),
                bound_conquer_points = table.Column<uint>(type: "int unsigned", nullable: false),
                pk_points = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                title_id = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                enlightenment_points = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                map_id = table.Column<uint>(type: "int unsigned", nullable: false),
                position_x = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                position_y = table.Column<ushort>(type: "smallint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_characters", x => x.character_id);
                table.CheckConstraint("CK_characters_account_id", "`account_id` > 0");
                table.CheckConstraint("CK_characters_appearance_composite", "`appearance_composite` > 0");
                table.CheckConstraint("CK_characters_level", "`level` BETWEEN 1 AND 140");
                table.CheckConstraint("CK_characters_map_id", "`map_id` > 0");
                table.CheckConstraint("CK_characters_name_length", "CHAR_LENGTH(`name`) BETWEEN 4 AND 15");
                table.CheckConstraint("CK_characters_profession", "`profession` > 0");
            })
            .Annotation("MySql:CharSet", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder.CreateTable(
            name: "schema_compatibility",
            columns: table => new
            {
                component_name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "ascii_bin")
                    .Annotation("MySql:CharSet", "ascii"),
                schema_version = table.Column<uint>(type: "int unsigned", nullable: false),
                migration_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "ascii_bin")
                    .Annotation("MySql:CharSet", "ascii"),
                applied_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schema_compatibility", x => x.component_name);
                table.CheckConstraint("CK_schema_compatibility_component_name", "CHAR_LENGTH(`component_name`) > 0");
                table.CheckConstraint("CK_schema_compatibility_migration_id", "CHAR_LENGTH(`migration_id`) > 0");
                table.CheckConstraint("CK_schema_compatibility_schema_version", "`schema_version` > 0");
            })
            .Annotation("MySql:CharSet", "ascii")
            .Annotation("Relational:Collation", "ascii_bin");

        migrationBuilder.CreateIndex(
            name: "UX_characters_account_id",
            table: "characters",
            column: "account_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_characters_name",
            table: "characters",
            column: "name",
            unique: true);

        migrationBuilder.Sql(
            """
            ALTER TABLE `characters`
            AUTO_INCREMENT = 1000000;
            """);

        migrationBuilder.Sql(
            """
            INSERT INTO `schema_compatibility`
                (`component_name`, `schema_version`, `migration_id`, `applied_at_utc`)
            VALUES
                ('game', 1, '20260914210346_InitialGameSchema', UTC_TIMESTAMP(6));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "characters");

        migrationBuilder.DropTable(
            name: "schema_compatibility");
    }
}
