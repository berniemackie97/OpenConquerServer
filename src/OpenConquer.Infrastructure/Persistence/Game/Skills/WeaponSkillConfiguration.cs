using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

internal sealed class WeaponSkillConfiguration : IEntityTypeConfiguration<WeaponSkillRecord>
{
    public void Configure(EntityTypeBuilder<WeaponSkillRecord> builder)
    {
        builder.ToTable("weapon_skills", table =>
        {
            table.HasCheckConstraint("CK_weapon_skills_owner_character_id", $"`owner_character_id` >= {CharacterIdentityPolicy.FirstPlayerEntityId}");
            table.HasCheckConstraint("CK_weapon_skills_level", $"`level` <= {WeaponSkillExperienceCurve.MaximumLevel}");
        });

        builder.HasKey(skill => new { skill.OwnerCharacterId, skill.WeaponSkillType }).HasName("PK_weapon_skills");

        builder.Property(skill => skill.OwnerCharacterId).HasColumnName("owner_character_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(skill => skill.WeaponSkillType).HasColumnName("weapon_skill_type").HasColumnType("int unsigned").IsRequired();
        builder.Property(skill => skill.Level).HasColumnName("level").HasColumnType("tinyint unsigned").IsRequired();
        builder.Property(skill => skill.Experience).HasColumnName("experience").HasColumnType("int unsigned").IsRequired();

        builder.HasOne<CharacterRecord>().WithMany().HasForeignKey(skill => skill.OwnerCharacterId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_weapon_skills_characters_owner_character_id");
    }
}
