using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Persistence.Game;

internal sealed class CharacterConfiguration : IEntityTypeConfiguration<CharacterRecord>
{
    public void Configure(EntityTypeBuilder<CharacterRecord> builder)
    {
        builder.ToTable("characters", table =>
            {
                table.HasCheckConstraint("CK_characters_account_id", "`account_id` > 0");
                table.HasCheckConstraint(
                    "CK_characters_name_length",
                    $"CHAR_LENGTH(`name`) BETWEEN {CharacterNamePolicy.MinimumEncodedLength} AND {CharacterNamePolicy.MaximumEncodedLength}");
                table.HasCheckConstraint("CK_characters_appearance_composite", "`appearance_composite` > 0");
                table.HasCheckConstraint(
                    "CK_characters_level",
                    $"`level` BETWEEN {CharacterProgressionPolicy.MinimumLevel} AND {CharacterProgressionPolicy.MaximumLevel}");
                table.HasCheckConstraint("CK_characters_profession", "`profession` > 0");
                table.HasCheckConstraint("CK_characters_map_id", "`map_id` > 0");
            });

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(character => character.CharacterId).HasName("PK_characters");

        builder.Property(character => character.CharacterId).HasColumnName("character_id").HasColumnType("int unsigned").ValueGeneratedOnAdd();
        builder.Property(character => character.AccountId).HasColumnName("account_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.Name).HasColumnName("name").HasMaxLength(CharacterNamePolicy.MaximumEncodedLength).HasCharSet("utf8mb4").UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(character => character.AppearanceComposite).HasColumnName("appearance_composite").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.HairComposite).HasColumnName("hair_composite").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.Level).HasColumnName("level").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(character => character.Experience).HasColumnName("experience").HasColumnType("bigint unsigned").IsRequired();
        builder.Property(character => character.Strength).HasColumnName("strength").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.Agility).HasColumnName("agility").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.Vitality).HasColumnName("vitality").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.Spirit).HasColumnName("spirit").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.UnspentAttributePoints).HasColumnName("unspent_attribute_points").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.CurrentLife).HasColumnName("current_life").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.CurrentMana).HasColumnName("current_mana").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.Profession).HasColumnName("profession").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(character => character.FirstProfession).HasColumnName("first_profession").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(character => character.PreviousProfession).HasColumnName("previous_profession").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(character => character.RebirthCount).HasColumnName("rebirth_count").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(character => character.Silver).HasColumnName("silver").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.ConquerPoints).HasColumnName("conquer_points").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.BoundConquerPoints).HasColumnName("bound_conquer_points").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.PkPoints).HasColumnName("pk_points").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.TitleId).HasColumnName("title_id").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.EnlightenmentPoints).HasColumnName("enlightenment_points").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.MapId).HasColumnName("map_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(character => character.PositionX).HasColumnName("position_x").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(character => character.PositionY).HasColumnName("position_y").HasColumnType("smallint unsigned").IsRequired();

        builder.HasIndex(character => character.AccountId).IsUnique().HasDatabaseName("UX_characters_account_id");
        builder.HasIndex(character => character.Name).IsUnique().HasDatabaseName("UX_characters_name");
    }
}
