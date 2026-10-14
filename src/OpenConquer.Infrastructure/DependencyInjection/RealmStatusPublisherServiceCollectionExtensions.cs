using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Infrastructure.Persistence.Accounts.Realms;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class RealmStatusPublisherServiceCollectionExtensions
{
    /// <summary>
    /// Registers the publisher a GameServer uses to report its realm status. Requires <see cref="AccountPersistenceServiceCollectionExtensions.AddAccountPersistence"/>.
    /// </summary>
    public static IServiceCollection AddRealmStatusPublisher(this IServiceCollection services, RealmId realmId)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (realmId.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(realmId), "Realm status publishing requires a nonzero realm identity.");
        }

        services.AddSingleton<IRealmStatusPublisher>(provider =>
            new MySqlRealmStatusPublisher(provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey), realmId));

        return services;
    }
}
