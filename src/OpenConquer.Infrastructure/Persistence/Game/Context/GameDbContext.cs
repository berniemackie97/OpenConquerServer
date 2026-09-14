using Microsoft.EntityFrameworkCore;
using OpenConquer.Infrastructure.Persistence.Schema;

namespace OpenConquer.Infrastructure.Persistence.Game.Context;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    internal DbSet<CharacterRecord> Characters => Set<CharacterRecord>();
    internal DbSet<SchemaCompatibilityRecord> SchemaCompatibility =>
        Set<SchemaCompatibilityRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_as_cs");

        modelBuilder.ApplyConfiguration(new CharacterConfiguration());
        modelBuilder.ApplyConfiguration(new SchemaCompatibilityConfiguration());
    }
}
