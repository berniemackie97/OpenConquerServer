using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Items;

namespace OpenConquer.Infrastructure.Persistence.Game.Items;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<ItemRecord>
{
    public void Configure(EntityTypeBuilder<ItemRecord> builder)
    {
        builder.ToTable("items", table =>
        {
            table.HasCheckConstraint("CK_items_item_type_id", "`item_type_id` > 0");
            table.HasCheckConstraint("CK_items_location_kind", $"`location_kind` IN ({(byte)ItemLocationKind.Inventory}, {(byte)ItemLocationKind.Equipment})");
            table.HasCheckConstraint("CK_items_location_payload",
                $"(`location_kind` = {(byte)ItemLocationKind.Inventory} AND `equipment_set` IS NULL AND `equipment_slot` IS NULL) OR "
                + $"(`location_kind` = {(byte)ItemLocationKind.Equipment} AND `equipment_set` IS NOT NULL AND `equipment_slot` IS NOT NULL)");
            table.HasCheckConstraint("CK_items_equipment_set",
                $"`equipment_set` IS NULL OR `equipment_set` IN ({(byte)EquipmentSet.Main}, {(byte)EquipmentSet.Alternate})");
            table.HasCheckConstraint("CK_items_equipment_slot",
                $"`equipment_slot` IS NULL OR `equipment_slot` BETWEEN {(byte)EquipmentSlot.Headwear} AND {(byte)EquipmentSlot.RidingCrop}");
            table.HasCheckConstraint("CK_items_alternate_equipment_slot",
                $"`equipment_set` IS NULL OR `equipment_set` <> {(byte)EquipmentSet.Alternate} OR `equipment_slot` BETWEEN {(byte)EquipmentSlot.Headwear} AND {(byte)EquipmentSlot.Garment}");
            table.HasCheckConstraint("CK_items_is_suspicious", "`is_suspicious` IN (0, 1)");
            table.HasCheckConstraint("CK_items_equipment_unlock_schedule",
                "((`equipment_lock_state_mask` & 2) = 0 AND `equipment_unlock_at_utc` IS NULL) OR "
                + "((`equipment_lock_state_mask` & 2) = 2 AND `equipment_unlock_at_utc` IS NOT NULL)");
            table.HasCheckConstraint("CK_items_stack_quantity", "`stack_quantity` >= 1");
        });

        builder.HasKey(item => item.ItemId).HasName("PK_items");

        builder.Property(item => item.ItemId).HasColumnName("item_id").HasColumnType("int unsigned").ValueGeneratedOnAdd();
        builder.Property(item => item.OwnerCharacterId).HasColumnName("owner_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.ItemTypeId).HasColumnName("item_type_id").HasColumnType("int unsigned").IsRequired();

        builder.Property(item => item.LocationKind).HasColumnName("location_kind").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.EquipmentSet).HasColumnName("equipment_set").HasColumnType("tinyint unsigned");
        builder.Property(item => item.EquipmentSlot).HasColumnName("equipment_slot").HasColumnType("tinyint unsigned");

        builder.Property(item => item.Durability).HasColumnName("durability").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(item => item.MaximumDurability).HasColumnName("maximum_durability").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(item => item.RetailCompatibilityByteA).HasColumnName("retail_compatibility_byte_a").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.TalismanSocketProgressOrSteedAppearanceColorOrMonsterKillCounterBaseline).HasColumnName("socket_progress_or_steed_color_or_monster_counter_baseline").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.Socket1Code).HasColumnName("socket1_code").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.Socket2Code).HasColumnName("socket2_code").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.HiddenAttackEffect).HasColumnName("hidden_attack_effect").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.RetailCompatibilityByteB).HasColumnName("retail_compatibility_byte_b").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.AdditionLevel).HasColumnName("addition_level").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.DamageReductionPercentOrSteedCompositionRed).HasColumnName("damage_reduction_percent_or_steed_composition_red").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.ItemBindingCode).HasColumnName("item_binding_code").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.EnchantmentLifeBonusOrSteedCompositionGreen).HasColumnName("enchantment_life_bonus_or_steed_composition_green").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(item => item.MonsterRestraintIdOrSteedCompositionBlue).HasColumnName("monster_restraint_id_or_steed_composition_blue").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.IsSuspicious).HasColumnName("is_suspicious").HasColumnType("tinyint(1)").IsRequired();
        builder.Property(item => item.EquipmentLockStateMask).HasColumnName("equipment_lock_state_mask").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(item => item.EquipmentUnlockAtUtc).HasColumnName("equipment_unlock_at_utc").HasColumnType("datetime(6)");
        builder.Property(item => item.EquipmentColor).HasColumnName("equipment_color").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(item => item.CompositionProgress).HasColumnName("composition_progress").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.InscribedSyndicateId).HasColumnName("inscribed_syndicate_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(item => item.StackQuantity).HasColumnName("stack_quantity").HasColumnType("smallint unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(item => item.OwnerCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_items_characters_owner_character_id");

        builder.HasIndex(item => new { item.OwnerCharacterId, item.LocationKind }).HasDatabaseName("IX_items_owner_character_id_location_kind");
        builder.HasIndex(item => new { item.OwnerCharacterId, item.EquipmentSet, item.EquipmentSlot }).IsUnique().HasDatabaseName("UX_items_owner_equipment_position");
    }
}
