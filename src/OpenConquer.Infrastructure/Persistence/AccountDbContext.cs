using Microsoft.EntityFrameworkCore;
using OpenConquer.Infrastructure.Persistence.Accounts;

namespace OpenConquer.Infrastructure.Persistence;

public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    internal DbSet<AccountRecord> Accounts => Set<AccountRecord>();
    internal DbSet<AccountPasswordCredentialRecord> AccountPasswordCredentials => Set<AccountPasswordCredentialRecord>();
    internal DbSet<AccountAuditEventRecord> AccountAuditEvents => Set<AccountAuditEventRecord>();
    internal DbSet<GameLoginTicketRecord> GameLoginTickets => Set<GameLoginTicketRecord>();
    internal DbSet<SchemaCompatibilityRecord> SchemaCompatibility => Set<SchemaCompatibilityRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_as_cs");

        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new AccountPasswordCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new AccountAuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new GameLoginTicketConfiguration());
        modelBuilder.ApplyConfiguration(new SchemaCompatibilityConfiguration());
    }
}
