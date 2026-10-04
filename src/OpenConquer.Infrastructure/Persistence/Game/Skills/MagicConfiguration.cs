using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

internal sealed class MagicConfiguration : IEntityTypeConfiguration<MagicRecord>
{
    public void Configure(EntityTypeBuilder<MagicRecord> builder)
    {
        builder.ToTable("magic", table =>
        {
            table.HasCheckConstraint("CK_magic_owner_character_id", $"`owner_character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
        });

        builder.HasKey(magic => new { magic.OwnerCharacterId, magic.MagicType }).HasName("PK_magic");

        builder.Property(magic => magic.OwnerCharacterId).HasColumnName("owner_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(magic => magic.MagicType).HasColumnName("magic_type").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(magic => magic.Level).HasColumnName("level").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(magic => magic.Experience).HasColumnName("experience").HasColumnType("int unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(magic => magic.OwnerCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_magic_characters_owner_character_id");
    }
}
