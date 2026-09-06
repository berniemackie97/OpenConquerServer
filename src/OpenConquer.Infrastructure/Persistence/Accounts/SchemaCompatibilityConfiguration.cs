using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class SchemaCompatibilityConfiguration
    : IEntityTypeConfiguration<SchemaCompatibilityRecord>
{
    internal const int MaximumComponentNameLength = 64;
    internal const int MaximumMigrationIdLength = 128;

    public void Configure(EntityTypeBuilder<SchemaCompatibilityRecord> builder)
    {
        builder.ToTable("schema_compatibility", table =>
            {
                table.HasCheckConstraint("CK_schema_compatibility_component_name", "CHAR_LENGTH(`component_name`) > 0");
                table.HasCheckConstraint("CK_schema_compatibility_schema_version", "`schema_version` > 0");
                table.HasCheckConstraint("CK_schema_compatibility_migration_id", "CHAR_LENGTH(`migration_id`) > 0");
            }
        );

        builder.HasCharSet("ascii").UseCollation("ascii_bin");
        builder.HasKey(compatibility => compatibility.ComponentName).HasName("PK_schema_compatibility");

        builder.Property(compatibility => compatibility.ComponentName).HasColumnName("component_name").HasMaxLength(MaximumComponentNameLength).HasCharSet("ascii").UseCollation("ascii_bin").IsRequired();
        builder.Property(compatibility => compatibility.SchemaVersion).HasColumnName("schema_version").HasColumnType("int unsigned").IsRequired();
        builder.Property(compatibility => compatibility.MigrationId).HasColumnName("migration_id").HasMaxLength(MaximumMigrationIdLength).HasCharSet("ascii").UseCollation("ascii_bin").IsRequired();
        builder.Property(compatibility => compatibility.AppliedAtUtc).HasColumnName("applied_at_utc").HasColumnType("datetime(6)").IsRequired();
    }
}
