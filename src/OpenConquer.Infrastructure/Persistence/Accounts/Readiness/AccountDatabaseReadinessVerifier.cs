using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using OpenConquer.Infrastructure.Persistence.Accounts;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Schema;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

public sealed class AccountDatabaseReadinessVerifier(IDbContextFactory<AccountDbContext> contextFactory) : IAccountDatabaseReadinessVerifier
{
    private readonly IDbContextFactory<AccountDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using AccountDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            string? characterSet = await db.Database.SqlQueryRaw<string>(
                    """
                    SELECT `DEFAULT_CHARACTER_SET_NAME` AS `Value`
                    FROM `INFORMATION_SCHEMA`.`SCHEMATA`
                    WHERE `SCHEMA_NAME` = DATABASE()
                    """
                ).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (characterSet is null)
            {
                throw new AccountDatabaseReadinessException("The configured account database could not be resolved in INFORMATION_SCHEMA.SCHEMATA.");
            }

            if (!string.Equals(characterSet, AccountSchemaContract.DatabaseCharacterSet, StringComparison.OrdinalIgnoreCase))
            {
                throw new AccountDatabaseReadinessException($"The account database character set is incompatible. Expected '{AccountSchemaContract.DatabaseCharacterSet}', but found '{characterSet}'.");
            }

            string? collation = await db.Database.SqlQueryRaw<string>(
                    """
                    SELECT `DEFAULT_COLLATION_NAME` AS `Value`
                    FROM `INFORMATION_SCHEMA`.`SCHEMATA`
                    WHERE `SCHEMA_NAME` = DATABASE()
                    """
                ).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (collation is null)
            {
                throw new AccountDatabaseReadinessException("The configured account database collation could not be resolved in INFORMATION_SCHEMA.SCHEMATA.");
            }

            if (!string.Equals(collation, AccountSchemaContract.DatabaseCollation, StringComparison.OrdinalIgnoreCase))
            {
                throw new AccountDatabaseReadinessException($"The account database collation is incompatible. Expected '{AccountSchemaContract.DatabaseCollation}', but found '{collation}'.");
            }

            SchemaCompatibilityRecord? compatibility = await db.SchemaCompatibility.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.ComponentName == AccountSchemaContract.ComponentName, cancellationToken).ConfigureAwait(false);

            if (compatibility is null)
            {
                throw new AccountDatabaseReadinessException($"The account database does not declare schema compatibility for component '{AccountSchemaContract.ComponentName}'.");
            }

            if (compatibility.SchemaVersion != AccountSchemaContract.SchemaVersion)
            {
                throw new AccountDatabaseReadinessException($"The account database schema version is incompatible. Expected {AccountSchemaContract.SchemaVersion}, but found {compatibility.SchemaVersion}.");
            }

            if (!string.Equals(compatibility.MigrationId, AccountSchemaContract.MigrationId, StringComparison.Ordinal))
            {
                throw new AccountDatabaseReadinessException($"The account database migration identity is incompatible. Expected '{AccountSchemaContract.MigrationId}', but found '{compatibility.MigrationId}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AccountDatabaseReadinessException)
        {
            throw;
        }
        catch (DbException exception)
        {
            throw new AccountDatabaseReadinessException("Account database readiness verification failed because the database could not be queried.", exception);
        }
    }
}
