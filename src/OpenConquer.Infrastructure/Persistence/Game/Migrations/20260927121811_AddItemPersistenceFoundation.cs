using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Game.Migrations;

/// <inheritdoc />
public partial class AddItemPersistenceFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "items",
            columns: table => new
            {
                item_id = table.Column<uint>(type: "int unsigned", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                owner_character_id = table.Column<uint>(type: "int unsigned", nullable: false),
                item_type_id = table.Column<uint>(type: "int unsigned", nullable: false),
                location_kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                equipment_set = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                equipment_slot = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                durability = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                maximum_durability = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                retail_compatibility_byte_a = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                socket_progress_or_steed_color_or_monster_counter_baseline = table.Column<uint>(type: "int unsigned", nullable: false),
                socket1_code = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                socket2_code = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                hidden_attack_effect = table.Column<uint>(type: "int unsigned", nullable: false),
                retail_compatibility_byte_b = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                addition_level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                damage_reduction_percent_or_steed_composition_red = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                item_binding_code = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                enchantment_life_bonus_or_steed_composition_green = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                monster_restraint_id_or_steed_composition_blue = table.Column<uint>(type: "int unsigned", nullable: false),
                is_suspicious = table.Column<bool>(type: "tinyint(1)", nullable: false),
                equipment_lock_state_mask = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                equipment_unlock_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                equipment_color = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                composition_progress = table.Column<uint>(type: "int unsigned", nullable: false),
                inscribed_syndicate_id = table.Column<uint>(type: "int unsigned", nullable: false),
                stack_quantity = table.Column<ushort>(type: "smallint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_items", x => x.item_id);
                table.CheckConstraint("CK_items_alternate_equipment_slot", "`equipment_set` IS NULL OR `equipment_set` <> 2 OR `equipment_slot` BETWEEN 1 AND 9");
                table.CheckConstraint("CK_items_equipment_set", "`equipment_set` IS NULL OR `equipment_set` IN (1, 2)");
                table.CheckConstraint("CK_items_equipment_slot", "`equipment_slot` IS NULL OR `equipment_slot` BETWEEN 1 AND 16");
                table.CheckConstraint("CK_items_equipment_unlock_schedule", "((`equipment_lock_state_mask` & 2) = 0 AND `equipment_unlock_at_utc` IS NULL) OR ((`equipment_lock_state_mask` & 2) = 2 AND `equipment_unlock_at_utc` IS NOT NULL)");
                table.CheckConstraint("CK_items_is_suspicious", "`is_suspicious` IN (0, 1)");
                table.CheckConstraint("CK_items_item_type_id", "`item_type_id` > 0");
                table.CheckConstraint("CK_items_location_kind", "`location_kind` IN (1, 2)");
                table.CheckConstraint("CK_items_location_payload", "(`location_kind` = 1 AND `equipment_set` IS NULL AND `equipment_slot` IS NULL) OR (`location_kind` = 2 AND `equipment_set` IS NOT NULL AND `equipment_slot` IS NOT NULL)");
                table.CheckConstraint("CK_items_stack_quantity", "`stack_quantity` >= 1");
                table.ForeignKey(
                    name: "FK_items_characters_owner_character_id",
                    column: x => x.owner_character_id,
                    principalTable: "characters",
                    principalColumn: "character_id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder.CreateIndex(
            name: "IX_items_owner_character_id_location_kind",
            table: "items",
            columns: new[] { "owner_character_id", "location_kind" });

        migrationBuilder.CreateIndex(
            name: "UX_items_owner_equipment_position",
            table: "items",
            columns: new[] { "owner_character_id", "equipment_set", "equipment_slot" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "items");
    }
}
