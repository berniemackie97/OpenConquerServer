using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Infrastructure.Persistence.Game.Social;

internal sealed class SocialRelationConfiguration : IEntityTypeConfiguration<SocialRelationRecord>
{
    public void Configure(EntityTypeBuilder<SocialRelationRecord> builder)
    {
        builder.ToTable("social_relations", table =>
        {
            table.HasCheckConstraint("CK_social_relations_owner_character_id", $"`owner_character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
            table.HasCheckConstraint("CK_social_relations_counterpart_character_id", $"`counterpart_character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
            table.HasCheckConstraint("CK_social_relations_distinct_characters", "`owner_character_id` <> `counterpart_character_id`");
            table.HasCheckConstraint("CK_social_relations_kind", $"`kind` IN ({(byte)SocialRelationKind.Friend}, {(byte)SocialRelationKind.Enemy})");
        });

        builder.HasKey(relation => new { relation.OwnerCharacterId, relation.Kind, relation.CounterpartCharacterId }).HasName("PK_social_relations");

        builder.Property(relation => relation.OwnerCharacterId).HasColumnName("owner_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(relation => relation.CounterpartCharacterId).HasColumnName("counterpart_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(relation => relation.Kind).HasColumnName("kind").HasColumnType("tinyint unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(relation => relation.OwnerCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_social_relations_characters_owner_character_id");

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(relation => relation.CounterpartCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_social_relations_characters_counterpart_character_id");

        builder.HasIndex(relation => new { relation.CounterpartCharacterId, relation.Kind, relation.OwnerCharacterId })
            .HasDatabaseName("IX_social_relations_counterpart_kind_owner");
    }
}
