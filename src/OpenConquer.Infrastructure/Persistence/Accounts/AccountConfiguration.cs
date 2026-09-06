using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<AccountRecord>
{
    public void Configure(EntityTypeBuilder<AccountRecord> builder)
    {
        builder.ToTable("accounts", table =>
            {
                table.HasCheckConstraint("CK_accounts_access_status", "`access_status` IN (1, 2, 3)");
                table.HasCheckConstraint("CK_accounts_authority_role", "`authority_role` IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint("CK_accounts_creation_operation_id", "`creation_operation_id` <> 0x00000000000000000000000000000000");
                table.HasCheckConstraint("CK_accounts_state_revision", "`state_revision` > 0");
                table.HasCheckConstraint("CK_accounts_created_actor", "((`created_by_actor_kind` = 2 AND `created_by_account_id` IS NOT NULL) OR (`created_by_actor_kind` IN (1, 3, 4) AND `created_by_account_id` IS NULL))");
                table.HasCheckConstraint("CK_accounts_state_changed_actor", "((`state_changed_by_actor_kind` = 2 AND `state_changed_by_account_id` IS NOT NULL) OR (`state_changed_by_actor_kind` IN (3, 4) AND `state_changed_by_account_id` IS NULL))");
                table.HasCheckConstraint("CK_accounts_deleted_state",
                    "((`deleted_at_utc` IS NULL "
                        + "AND `deleted_by_actor_kind` IS NULL "
                        + "AND `deleted_by_account_id` IS NULL) "
                        + "OR (`deleted_at_utc` IS NOT NULL "
                        + "AND `deleted_at_utc` = `state_changed_at_utc` "
                        + "AND `deleted_by_actor_kind` IS NOT NULL "
                        + "AND `deleted_by_actor_kind` = `state_changed_by_actor_kind` "
                        + "AND (`deleted_by_account_id` <=> `state_changed_by_account_id`) "
                        + "AND ((`deleted_by_actor_kind` = 2 "
                        + "AND `deleted_by_account_id` IS NOT NULL) "
                        + "OR (`deleted_by_actor_kind` IN (3, 4) "
                        + "AND `deleted_by_account_id` IS NULL))))"
                );
                table.HasCheckConstraint("CK_accounts_last_successful_login_at", "`last_successful_login_at_utc` IS NULL OR `last_successful_login_at_utc` >= `created_at_utc`");
                table.HasCheckConstraint("CK_accounts_state_changed_at", "`state_changed_at_utc` >= `created_at_utc`");
            }
        );

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(account => account.AccountId).HasName("PK_accounts");

        builder.Property(account => account.AccountId).HasColumnName("account_id").HasColumnType("int unsigned").ValueGeneratedOnAdd();
        builder.Property(account => account.Username).HasColumnName("username").HasMaxLength(AccountCredentialPolicy.MaximumUsernameLength).HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci").IsRequired();
        builder.Property(account => account.AccessStatus).HasColumnName("access_status").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(account => account.AuthorityRole).HasColumnName("authority_role").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(account => account.CreationOperationId).HasColumnName("creation_operation_id").HasColumnType("binary(16)").IsRequired();
        builder.Property(account => account.LastSuccessfulLoginAtUtc).HasColumnName("last_successful_login_at_utc").HasColumnType("datetime(6)");
        builder.Property(account => account.StateRevision).HasColumnName("state_revision").HasColumnType("bigint unsigned").IsConcurrencyToken();
        builder.Property(account => account.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(account => account.CreatedByActorKind).HasColumnName("created_by_actor_kind").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(account => account.CreatedByAccountId).HasColumnName("created_by_account_id").HasColumnType("int unsigned");
        builder.Property(account => account.StateChangedAtUtc).HasColumnName("state_changed_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(account => account.StateChangedByActorKind).HasColumnName("state_changed_by_actor_kind").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(account => account.StateChangedByAccountId).HasColumnName("state_changed_by_account_id").HasColumnType("int unsigned");
        builder.Property(account => account.DeletedAtUtc).HasColumnName("deleted_at_utc").HasColumnType("datetime(6)");
        builder.Property(account => account.DeletedByActorKind).HasColumnName("deleted_by_actor_kind").HasColumnType("tinyint unsigned").HasConversion<byte>();
        builder.Property(account => account.DeletedByAccountId).HasColumnName("deleted_by_account_id").HasColumnType("int unsigned");

        builder.HasIndex(account => account.Username).IsUnique().HasDatabaseName("UX_accounts_username");
        builder.HasIndex(account => account.CreationOperationId).IsUnique().HasDatabaseName("UX_accounts_creation_operation_id");
        builder.HasIndex(account => account.CreatedByAccountId).HasDatabaseName("IX_accounts_created_by_account_id");
        builder.HasIndex(account => account.StateChangedByAccountId).HasDatabaseName("IX_accounts_state_changed_by_account_id");
        builder.HasIndex(account => account.DeletedByAccountId).HasDatabaseName("IX_accounts_deleted_by_account_id");

        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(account => account.CreatedByAccountId).HasConstraintName("FK_accounts_created_by_account").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(account => account.StateChangedByAccountId).HasConstraintName("FK_accounts_state_changed_by_account").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(account => account.DeletedByAccountId).HasConstraintName("FK_accounts_deleted_by_account").OnDelete(DeleteBehavior.Restrict);
    }
}
