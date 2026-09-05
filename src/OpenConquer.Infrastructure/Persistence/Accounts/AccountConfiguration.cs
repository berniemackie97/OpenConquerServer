using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<AccountRecord>
{
    internal const int MaximumPasswordHashLength = 255;

    public void Configure(EntityTypeBuilder<AccountRecord> builder)
    {
        builder.ToTable("accounts");
        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).HasColumnName("uid").ValueGeneratedOnAdd();
        builder.Property(account => account.Username).HasColumnName("username")
            .HasMaxLength(AccountCredentialPolicy.MaximumUsernameLength).IsRequired();
        builder.Property(account => account.PasswordHash).HasColumnName("password")
            .HasMaxLength(MaximumPasswordHashLength).IsRequired();
        builder.Property(account => account.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(account => account.EmailVerification).HasColumnName("email_ver").HasMaxLength(255).IsRequired();
        builder.Property(account => account.EmailStatus).HasColumnName("email_status").HasDefaultValue((byte)0);
        builder.Property(account => account.SecurityAnswer).HasColumnName("security_answer").HasMaxLength(128).IsRequired();
        builder.Property(account => account.SecurityQuestion).HasColumnName("security_question").HasMaxLength(128).IsRequired();
        builder.Property(account => account.Permission).HasColumnName("permission").HasDefaultValue(0u);
        builder.Property(account => account.LoginTimestamp).HasColumnName("timestamp_token").HasDefaultValue(0u);
        builder.Property(account => account.RegistrationOperationId).HasColumnName("registration_operation_id")
            .HasColumnType("char(64)").HasMaxLength(64).HasCharSet("ascii").UseCollation("ascii_bin");
        builder.HasIndex(account => account.Username).IsUnique().HasDatabaseName("UX_accounts_username");
        builder.HasIndex(account => account.RegistrationOperationId).IsUnique()
            .HasDatabaseName("UX_accounts_registration_operation_id");
        builder.HasIndex(account => account.Permission).HasDatabaseName("IX_accounts_permission");
    }
}
