using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenConquer.Domain.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Persistence.Accounts;

internal sealed class GameLoginTicketConfiguration : IEntityTypeConfiguration<GameLoginTicketRecord>
{
    public void Configure(EntityTypeBuilder<GameLoginTicketRecord> builder)
    {
        builder.ToTable("game_login_tickets", table =>
            {
                table.HasCheckConstraint("CK_game_login_tickets_expiration", "`expires_at_utc` > `issued_at_utc`");
                table.HasCheckConstraint("CK_game_login_tickets_session_uid", "`session_uid` > 0");
                table.HasCheckConstraint("CK_game_login_tickets_verifier_key_id", "`authentication_key_verifier_key_id` > 0");
            }
        );

        builder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_as_cs");
        builder.HasKey(ticket => ticket.SessionUid).HasName("PK_game_login_tickets");

        builder.Property(ticket => ticket.SessionUid).HasColumnName("session_uid").HasColumnType("int unsigned").ValueGeneratedNever();
        builder.Property(ticket => ticket.AuthenticationKeyVerifier).HasColumnName("authentication_key_verifier").HasColumnType("binary(32)").IsFixedLength().IsRequired();
        builder.Property(ticket => ticket.AuthenticationKeyVerifierKeyId).HasColumnName("authentication_key_verifier_key_id").HasColumnType("smallint unsigned").IsRequired();
        builder.Property(ticket => ticket.AccountId).HasColumnName("account_id").HasColumnType("int unsigned").IsRequired();
        builder.Property(ticket => ticket.Username).HasColumnName("username").HasMaxLength(AccountCredentialPolicy.MaximumUsernameLength).HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci").IsRequired();
        builder.Property(ticket => ticket.IssuedAtUtc).HasColumnName("issued_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(ticket => ticket.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("datetime(6)").IsRequired();

        builder.HasIndex(ticket => ticket.AccountId).HasDatabaseName("IX_game_login_tickets_account_id");
        builder.HasIndex(ticket => ticket.ExpiresAtUtc).HasDatabaseName("IX_game_login_tickets_expires_at_utc");

        builder.HasOne(ticket => ticket.Account).WithMany().HasForeignKey(ticket => ticket.AccountId).HasConstraintName("FK_game_login_tickets_account").OnDelete(DeleteBehavior.Cascade);
    }
}
