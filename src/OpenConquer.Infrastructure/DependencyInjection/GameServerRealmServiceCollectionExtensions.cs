using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Persistence.Accounts.Realms;
using OpenConquer.Infrastructure.Persistence.Accounts.Sessions;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class GameServerRealmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Accounts database services a GameServer uses on behalf of its realm: status reporting and
    /// the account session record. Requires <see cref="AccountPersistenceServiceCollectionExtensions.AddAccountPersistence"/>.
    /// </summary>
    public static IServiceCollection AddGameServerRealmServices(this IServiceCollection services, RealmId realmId)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (realmId.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(realmId), "GameServer realm services require a nonzero realm identity.");
        }

        services.AddSingleton<IRealmStatusPublisher>(provider =>
            new MySqlRealmStatusPublisher(provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey), realmId));
        services.AddSingleton<IAccountSessionStore>(provider =>
            new MySqlAccountSessionStore(provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey), realmId));

        return services;
    }
}
