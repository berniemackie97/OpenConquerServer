using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Schema;

namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

public sealed class GameDatabaseReadinessVerifier(IDbContextFactory<GameDbContext> contextFactory) : IGameDatabaseReadinessVerifier
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            string? characterSet = await db.Database.SqlQueryRaw<string>(
                """
                SELECT `DEFAULT_CHARACTER_SET_NAME` AS `Value`
                FROM `INFORMATION_SCHEMA`.`SCHEMATA`
                WHERE `SCHEMA_NAME` = DATABASE()
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (characterSet is null)
            {
                throw new GameDatabaseReadinessException("The configured game database could not be resolved in INFORMATION_SCHEMA.SCHEMATA.");
            }

            if (!string.Equals(characterSet, GameSchemaContract.DatabaseCharacterSet, StringComparison.OrdinalIgnoreCase))
            {
                throw new GameDatabaseReadinessException($"The game database character set is incompatible. Expected '{GameSchemaContract.DatabaseCharacterSet}', but found '{characterSet}'.");
            }

            string? collation = await db.Database.SqlQueryRaw<string>(
                """
                SELECT `DEFAULT_COLLATION_NAME` AS `Value`
                FROM `INFORMATION_SCHEMA`.`SCHEMATA`
                WHERE `SCHEMA_NAME` = DATABASE()
                """).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (collation is null)
            {
                throw new GameDatabaseReadinessException("The configured game database collation could not be resolved in INFORMATION_SCHEMA.SCHEMATA.");
            }

            if (!string.Equals(collation, GameSchemaContract.DatabaseCollation, StringComparison.OrdinalIgnoreCase))
            {
                throw new GameDatabaseReadinessException($"The game database collation is incompatible. Expected '{GameSchemaContract.DatabaseCollation}', but found '{collation}'.");
            }

            SchemaCompatibilityRecord? compatibility = await db.SchemaCompatibility.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.ComponentName == GameSchemaContract.ComponentName, cancellationToken)
                .ConfigureAwait(false);

            if (compatibility is null)
            {
                throw new GameDatabaseReadinessException($"The game database does not declare schema compatibility for component '{GameSchemaContract.ComponentName}'.");
            }

            if (compatibility.SchemaVersion != GameSchemaContract.SchemaVersion)
            {
                throw new GameDatabaseReadinessException($"The game database schema version is incompatible. Expected {GameSchemaContract.SchemaVersion}, but found {compatibility.SchemaVersion}.");
            }

            if (!string.Equals(compatibility.MigrationId, GameSchemaContract.MigrationId, StringComparison.Ordinal))
            {
                throw new GameDatabaseReadinessException($"The game database migration identity is incompatible. Expected '{GameSchemaContract.MigrationId}', but found '{compatibility.MigrationId}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GameDatabaseReadinessException)
        {
            throw;
        }
        catch (DbException exception)
        {
            throw new GameDatabaseReadinessException("Game database readiness verification failed because the database could not be queried.", exception);
        }
    }
}
