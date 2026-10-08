using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Persistence.Game.Syndicates;

internal sealed class SyndicateMembershipConfiguration : IEntityTypeConfiguration<SyndicateMembershipRecord>
{
    public void Configure(EntityTypeBuilder<SyndicateMembershipRecord> builder)
    {
        builder.ToTable("syndicate_memberships", table =>
        {
            table.HasCheckConstraint("CK_syndicate_memberships_character_id",
                $"`character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
            table.HasCheckConstraint("CK_syndicate_memberships_syndicate_id", "`syndicate_id` > 0");
        });

        builder.HasKey(membership => membership.CharacterId).HasName("PK_syndicate_memberships");

        builder.Property(membership => membership.CharacterId).HasColumnName("character_id").HasColumnType("int unsigned").ValueGeneratedNever();
        builder.Property(membership => membership.SyndicateId).HasColumnName("syndicate_id").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(membership => membership.Rank).HasColumnName("rank").HasColumnType("int unsigned").IsRequired();
        builder.Property(membership => membership.Proffer).HasColumnName("proffer").HasColumnType("int unsigned").IsRequired();
        builder.Property(membership => membership.PositionExpirationUnixSeconds).HasColumnName("position_expiration_unix_seconds")
            .HasColumnType("int unsigned").IsRequired();
        builder.Property(membership => membership.JoinDateUnixSeconds).HasColumnName("join_date_unix_seconds")
            .HasColumnType("int unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(membership => membership.CharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_syndicate_memberships_characters_character_id");

        builder.HasOne<SyndicateRecord>().WithMany().HasForeignKey(membership => membership.SyndicateId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_syndicate_memberships_syndicates_syndicate_id");

        builder.HasIndex(membership => new { membership.SyndicateId, membership.CharacterId })
            .HasDatabaseName("IX_syndicate_memberships_syndicate_id_character_id");
    }
}
