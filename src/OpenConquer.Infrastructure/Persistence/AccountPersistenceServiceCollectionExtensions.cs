using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Infrastructure.Persistence.Accounts;

namespace OpenConquer.Infrastructure.Persistence;

public static class AccountPersistenceServiceCollectionExtensions
{
    /// <summary>Registers pooled, operation-scoped contexts for the MySQL 8.4 account database.</summary>
    public static IServiceCollection AddAccountPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Login completion must count matching rows, even when two logins share a timestamp.
        MySqlConnectionStringBuilder connection = new(connectionString) { UseAffectedRows = false };

        services.AddPooledDbContextFactory<AccountDbContext>(options => options
            .UseMySql(connection.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0)), mysql => mysql
                .EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
        services.AddSingleton<IAccountAuthenticationRepository, AccountAuthenticationRepository>();

        return services;
    }
}
