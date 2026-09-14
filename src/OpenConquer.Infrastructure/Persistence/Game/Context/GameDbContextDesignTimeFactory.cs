using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenConquer.Infrastructure.Persistence.Game.Context;

internal sealed class GameDbContextDesignTimeFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    private const string MigrationScaffoldingConnectionString = "Server=localhost;Database=openconquer_game;User ID=openconquer_migration_scaffolding;";

    public GameDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<GameDbContext> options = new();
        GameDbContextOptionsConfiguration.Configure(options, MigrationScaffoldingConnectionString);

        return new GameDbContext(options.Options);
    }
}
