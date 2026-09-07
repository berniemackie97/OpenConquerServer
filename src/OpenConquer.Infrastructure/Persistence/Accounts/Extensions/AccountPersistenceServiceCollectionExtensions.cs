using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts.Context;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;

namespace OpenConquer.Infrastructure.Persistence.Accounts.Extensions;

public static class AccountPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAccountPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddPooledDbContextFactory<AccountDbContext>(options => AccountDbContextOptionsConfiguration.Configure(options, connectionString));

        services.AddSingleton<IAccountAuthenticationRepository, AccountAuthenticationRepository>();
        services.AddSingleton<AccountDatabaseReadinessVerifier>();

        return services;
    }
}
