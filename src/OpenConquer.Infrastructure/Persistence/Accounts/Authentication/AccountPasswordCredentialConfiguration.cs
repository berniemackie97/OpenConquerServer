using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Authentication;

internal sealed class AccountPasswordCredentialConfiguration : IEntityTypeConfiguration<AccountPasswordCredentialRecord>
{
    internal const int MaximumPasswordHashLength = 255;

    public void Configure(EntityTypeBuilder<AccountPasswordCredentialRecord> builder)
    {
        builder.ToTable("account_password_credentials", table =>
            {
                table.HasCheckConstraint("CK_account_password_credentials_password_hash", "CHAR_LENGTH(`password_hash`) > 0");
                table.HasCheckConstraint("CK_account_password_credentials_revision", "`revision` > 0");
            }
        );

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(credential => credential.AccountId).HasName("PK_account_password_credentials");

        builder.Property(credential => credential.AccountId).HasColumnName("account_id").HasColumnType("int unsigned").ValueGeneratedNever();
        builder.Property(credential => credential.PasswordHash).HasColumnName("password_hash").HasMaxLength(MaximumPasswordHashLength).HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_bin").IsRequired();
        builder.Property(credential => credential.PasswordChangedAtUtc).HasColumnName("password_changed_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(credential => credential.Revision).HasColumnName("revision").HasColumnType("bigint unsigned").IsConcurrencyToken();

        builder.HasOne(credential => credential.Account).WithOne(account => account.PasswordCredential).HasForeignKey<AccountPasswordCredentialRecord>(credential => credential.AccountId).HasConstraintName("FK_account_password_credentials_account").OnDelete(DeleteBehavior.Cascade);
    }
}
