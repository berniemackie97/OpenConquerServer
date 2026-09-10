using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.Mutations;
using OpenConquer.Infrastructure.Persistence.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Mutations;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Extensions;

public static class AccountPersistenceServiceCollectionExtensions
{
    internal const string RawMySqlDataSourceKey = "OpenConquer.Accounts.RawMySql";

    public static IServiceCollection AddAccountPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddPooledDbContextFactory<AccountDbContext>(options => AccountDbContextOptionsConfiguration.Configure(options, connectionString));

        services.AddKeyedSingleton<MySqlDataSource>(RawMySqlDataSourceKey, (_, _) =>
        {
            MySqlConnectionStringBuilder connection = new(AccountDbContextOptionsConfiguration.CreateConnectionString(connectionString))
            {
                AutoEnlist = false,
            };

            return new MySqlDataSource(connection.ConnectionString);
        });

        services.AddSingleton<IAccountAuthenticationRepository, AccountAuthenticationRepository>();
        services.AddSingleton<IAccountMutationStore>(provider => new AccountMutationStore(provider.GetRequiredKeyedService<MySqlDataSource>(RawMySqlDataSourceKey)));
        services.AddSingleton<AccountDatabaseReadinessVerifier>();
        services.AddSingleton<IAccountDatabaseReadinessVerifier>(provider => provider.GetRequiredService<AccountDatabaseReadinessVerifier>());

        GameLoginTicketExpirationCleanerOptions expirationCleanerOptions = new();

        services.AddSingleton(expirationCleanerOptions);
        services.AddSingleton<GameLoginTicketExpirationCleaner>(provider => new GameLoginTicketExpirationCleaner(provider.GetRequiredKeyedService<MySqlDataSource>(RawMySqlDataSourceKey), provider.GetRequiredService<GameLoginTicketExpirationCleanerOptions>()));
        services.AddSingleton<IGameLoginTicketExpirationCleaner>(provider => provider.GetRequiredService<GameLoginTicketExpirationCleaner>());

        return services;
    }
}
