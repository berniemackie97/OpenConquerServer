using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenConquer.Infrastructure.Persistence;

internal sealed class AccountDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AccountDbContext>
{
    private const string MigrationScaffoldingConnectionString = "Server=localhost;Database=openconquer_accounts;User ID=openconquer_migration_scaffolding;";

    public AccountDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AccountDbContext> options = new();
        AccountDbContextOptionsConfiguration.Configure(options, MigrationScaffoldingConnectionString);

        return new AccountDbContext(options.Options);
    }
}
