using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Audit;

internal sealed class AccountAuditEventConfiguration : IEntityTypeConfiguration<AccountAuditEventRecord>
{
    internal const int MaximumReasonCodeLength = 64;

    public void Configure(EntityTypeBuilder<AccountAuditEventRecord> builder)
    {
        builder.ToTable("account_audit_events", table =>
        {
            table.HasCheckConstraint("CK_account_audit_events_event_kind", "`event_kind` IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
            table.HasCheckConstraint("CK_account_audit_events_actor", "((`event_kind` = 1 AND `actor_kind` = 1 AND `actor_account_id` IS NULL) OR (`actor_kind` = 2 AND `actor_account_id` IS NOT NULL) OR (`actor_kind` IN (3, 4) AND `actor_account_id` IS NULL))");
            table.HasCheckConstraint("CK_account_audit_events_correlation_id", "`correlation_id` <> 0x00000000000000000000000000000000");
            table.HasCheckConstraint("CK_account_audit_events_reason_code", "`reason_code` IS NULL OR CHAR_LENGTH(`reason_code`) > 0");
            table.HasCheckConstraint("CK_account_audit_events_previous_access_status", "`previous_access_status` IS NULL OR `previous_access_status` IN (1, 2, 3)");
            table.HasCheckConstraint("CK_account_audit_events_new_access_status", "`new_access_status` IS NULL OR `new_access_status` IN (1, 2, 3)");
            table.HasCheckConstraint("CK_account_audit_events_previous_authority_role", "`previous_authority_role` IS NULL OR `previous_authority_role` IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_account_audit_events_new_authority_role", "`new_authority_role` IS NULL OR `new_authority_role` IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_account_audit_events_payload",
                "((`event_kind` = 1 " +
                "AND `previous_access_status` IS NULL " +
                "AND `new_access_status` IS NOT NULL " +
                "AND `new_access_status` IN (1, 2, 3) " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NOT NULL " +
                "AND `new_authority_role` IN (1, 2, 3, 4, 5)) " +
                "OR (`event_kind` = 2 " +
                "AND `previous_access_status` IS NOT NULL " +
                "AND `previous_access_status` = 1 " +
                "AND `new_access_status` IS NOT NULL " +
                "AND `new_access_status` = 2 " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NULL) " +
                "OR (`event_kind` = 3 " +
                "AND `previous_access_status` IS NOT NULL " +
                "AND `previous_access_status` = 2 " +
                "AND `new_access_status` IS NOT NULL " +
                "AND `new_access_status` = 1 " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NULL) " +
                "OR (`event_kind` = 4 " +
                "AND `previous_access_status` IS NOT NULL " +
                "AND `previous_access_status` IN (1, 2) " +
                "AND `new_access_status` IS NOT NULL " +
                "AND `new_access_status` = 3 " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NULL) " +
                "OR (`event_kind` = 5 " +
                "AND `previous_access_status` IS NOT NULL " +
                "AND `previous_access_status` = 3 " +
                "AND `new_access_status` IS NOT NULL " +
                "AND `new_access_status` IN (1, 2) " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NULL) " +
                "OR (`event_kind` IN (6, 7, 8, 9) " +
                "AND `previous_access_status` IS NULL " +
                "AND `new_access_status` IS NULL " +
                "AND `previous_authority_role` IS NULL " +
                "AND `new_authority_role` IS NULL) " +
                "OR (`event_kind` = 10 " +
                "AND `previous_access_status` IS NULL " +
                "AND `new_access_status` IS NULL " +
                "AND `previous_authority_role` IS NOT NULL " +
                "AND `previous_authority_role` IN (1, 2, 3, 4, 5) " +
                "AND `new_authority_role` IS NOT NULL " +
                "AND `new_authority_role` IN (1, 2, 3, 4, 5) " +
                "AND `previous_authority_role` <> `new_authority_role`))");
        });

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(auditEvent => auditEvent.AccountAuditEventId).HasName("PK_account_audit_events");

        builder.Property(auditEvent => auditEvent.AccountAuditEventId).HasColumnName("account_audit_event_id").HasColumnType("bigint unsigned").ValueGeneratedOnAdd();
        builder.Property(auditEvent => auditEvent.AccountId).HasColumnName("account_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(auditEvent => auditEvent.EventKind).HasColumnName("event_kind").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(auditEvent => auditEvent.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(auditEvent => auditEvent.ActorKind).HasColumnName("actor_kind").HasColumnType("tinyint unsigned").HasConversion<byte>().IsRequired();
        builder.Property(auditEvent => auditEvent.ActorAccountId).HasColumnName("actor_account_id").HasColumnType("int unsigned");
        builder.Property(auditEvent => auditEvent.CorrelationId).HasColumnName("correlation_id").HasColumnType("binary(16)").IsRequired();
        builder.Property(auditEvent => auditEvent.ReasonCode).HasColumnName("reason_code").HasMaxLength(MaximumReasonCodeLength).HasCharSet("ascii").UseCollation("ascii_bin");
        builder.Property(auditEvent => auditEvent.PreviousAccessStatus).HasColumnName("previous_access_status").HasColumnType("tinyint unsigned").HasConversion<byte>();
        builder.Property(auditEvent => auditEvent.NewAccessStatus).HasColumnName("new_access_status").HasColumnType("tinyint unsigned").HasConversion<byte>();
        builder.Property(auditEvent => auditEvent.PreviousAuthorityRole).HasColumnName("previous_authority_role").HasColumnType("tinyint unsigned").HasConversion<byte>();
        builder.Property(auditEvent => auditEvent.NewAuthorityRole).HasColumnName("new_authority_role").HasColumnType("tinyint unsigned").HasConversion<byte>();

        builder.HasIndex(auditEvent => new
        {
            auditEvent.AccountId,
            auditEvent.OccurredAtUtc,
        }).HasDatabaseName("IX_account_audit_events_account_id_occurred_at_utc");

        builder.HasIndex(auditEvent => auditEvent.CorrelationId).HasDatabaseName("IX_account_audit_events_correlation_id");
        builder.HasIndex(auditEvent => auditEvent.ActorAccountId).HasDatabaseName("IX_account_audit_events_actor_account_id");

        builder.HasOne(auditEvent => auditEvent.Account).WithMany().HasForeignKey(auditEvent => auditEvent.AccountId).HasConstraintName("FK_account_audit_events_account").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(auditEvent => auditEvent.ActorAccountId).HasConstraintName("FK_account_audit_events_actor_account").OnDelete(DeleteBehavior.Restrict);
    }
}
