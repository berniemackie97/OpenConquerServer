using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Readiness;

public sealed class GameRealmBindingVerifier(IDbContextFactory<GameDbContext> contextFactory) : IGameRealmBindingVerifier
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async Task VerifyAsync(RealmId expectedRealmId, CancellationToken cancellationToken = default)
    {
        if (expectedRealmId.Value == 0)
        {
            throw new ArgumentException("GameServer realm verification requires a nonzero configured realm identity.", nameof(expectedRealmId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            uint persistedRealmId = await db.Database.SqlQueryRaw<uint>(
                "SELECT `realm_id` AS `Value` FROM `realm_binding` WHERE `binding_key` = 1")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (persistedRealmId == 0)
            {
                throw new GameDatabaseReadinessException("The Game database has no provisioned realm identity. The GameServer cannot start.");
            }

            if (persistedRealmId != expectedRealmId.Value)
            {
                throw new GameDatabaseReadinessException($"The Game database is bound to realm {persistedRealmId}, but this GameServer is configured for realm {expectedRealmId.Value}.");
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
            throw new GameDatabaseReadinessException("Game database realm-binding verification failed because the database could not be queried.", exception);
        }
    }
}
