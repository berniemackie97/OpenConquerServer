using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Accounts.Extensions;

public static class AccountLoginInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAccountLoginInfrastructure(this IServiceCollection services, string connectionString, AccountAuthenticationProtectionOptions authenticationProtectionOptions, ushort activeVerificationKeyId, IEnumerable<KeyValuePair<ushort, string>> encodedVerificationKeys)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(authenticationProtectionOptions);
        ArgumentNullException.ThrowIfNull(encodedVerificationKeys);

        KeyValuePair<ushort, string>[] verificationKeys = encodedVerificationKeys.ToArray();

        GameLoginTicketAuthenticationKeyRingFactory.ValidateConfiguration(activeVerificationKeyId, verificationKeys);

        services.AddAccountPersistence(connectionString);
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);

        services.AddSingleton(authenticationProtectionOptions);
        services.AddSingleton<AccountAuthenticationProtection>();
        services.AddSingleton<IAccountAuthenticationRequestLimiter>(provider => provider.GetRequiredService<AccountAuthenticationProtection>());
        services.AddSingleton<IAccountAuthenticationAttemptLimiter>(provider => provider.GetRequiredService<AccountAuthenticationProtection>());

        services.AddSingleton<IAccountPasswordHasher, AccountPasswordHasher>();
        services.AddSingleton<IAccountAuthenticator, AccountAuthenticator>();

        services.AddSingleton(provider => GameLoginTicketAuthenticationKeyRingFactory.Create(activeVerificationKeyId, verificationKeys));
        services.AddSingleton<IGameLoginTicketGrantStore>(provider => new GameLoginTicketGrantStore(provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey), provider.GetRequiredService<GameLoginTicketAuthenticationKeyRing>()));
        services.AddSingleton<IGameLoginTicketTokenGenerator, CryptographicGameLoginTicketTokenGenerator>();
        services.AddSingleton<GameLoginTicketIssuer>();

        return services;
    }
}
