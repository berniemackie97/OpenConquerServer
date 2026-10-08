using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Infrastructure.Persistence.Game.Syndicates;

internal sealed class SyndicateConfiguration : IEntityTypeConfiguration<SyndicateRecord>
{
    public void Configure(EntityTypeBuilder<SyndicateRecord> builder)
    {
        builder.ToTable("syndicates", table =>
        {
            table.HasCheckConstraint("CK_syndicates_name_length", $"CHAR_LENGTH(`name`) BETWEEN {SyndicateNamePolicy.MinimumEncodedLength} AND {SyndicateNamePolicy.MaximumEncodedLength}");
            table.HasCheckConstraint("CK_syndicates_leader_character_id", $"`leader_character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
        });

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(syndicate => syndicate.SyndicateId).HasName("PK_syndicates");

        builder.Property(syndicate => syndicate.SyndicateId).HasColumnName("syndicate_id").HasColumnType("smallint unsigned").ValueGeneratedOnAdd();
        builder.Property(syndicate => syndicate.Name).HasColumnName("name").HasMaxLength(SyndicateNamePolicy.MaximumEncodedLength).HasCharSet("utf8mb4").UseCollation("utf8mb4_bin").IsRequired();
        builder.Property(syndicate => syndicate.LeaderCharacterId).HasColumnName("leader_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(syndicate => syndicate.SilverFund).HasColumnName("silver_fund").HasColumnType("bigint unsigned").IsRequired();
        builder.Property(syndicate => syndicate.EmoneyFund).HasColumnName("emoney_fund").HasColumnType("int unsigned").IsRequired();
        builder.Property(syndicate => syndicate.RequiredLevel).HasColumnName("required_level").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(syndicate => syndicate.RequiredProfession).HasColumnName("required_profession").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(syndicate => syndicate.RequiredMetempsychosis).HasColumnName("required_metempsychosis").HasColumnType("tinyint unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(syndicate => syndicate.LeaderCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_syndicates_characters_leader_character_id");

        builder.HasIndex(syndicate => syndicate.Name).IsUnique().HasDatabaseName("UX_syndicates_name");
        builder.HasIndex(syndicate => syndicate.LeaderCharacterId).IsUnique().HasDatabaseName("UX_syndicates_leader_character_id");
    }
}
