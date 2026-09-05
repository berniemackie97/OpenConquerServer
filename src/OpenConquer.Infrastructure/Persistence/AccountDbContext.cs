using Microsoft.EntityFrameworkCore;
using OpenConquer.Infrastructure.Persistence.Accounts;

namespace OpenConquer.Infrastructure.Persistence;

/// <summary>Maps account storage independently of application and domain models.</summary>
public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    internal DbSet<AccountRecord> Accounts => Set<AccountRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
    }
}
