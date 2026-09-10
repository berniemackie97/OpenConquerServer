using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Extensions;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Accounts.Extensions;

public static class GameLoginTicketRedemptionInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGameLoginTicketRedemptionInfrastructure(this IServiceCollection services, string connectionString,
        GameLoginTicketRedemptionAttemptLimiterOptions redemptionProtectionOptions, ushort activeVerificationKeyId,
        IEnumerable<KeyValuePair<ushort, string>> encodedVerificationKeys)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(redemptionProtectionOptions);
        ArgumentNullException.ThrowIfNull(encodedVerificationKeys);

        KeyValuePair<ushort, string>[] verificationKeys = encodedVerificationKeys.ToArray();

        GameLoginTicketAuthenticationKeyRingFactory.ValidateConfiguration(activeVerificationKeyId, verificationKeys);

        services.AddAccountPersistence(connectionString);
        services.TryAddSingleton<TimeProvider>(_ => TimeProvider.System);

        services.AddSingleton(redemptionProtectionOptions);
        services.AddSingleton<GameLoginTicketRedemptionAttemptLimiter>();
        services.AddSingleton<IGameLoginTicketRedemptionAttemptLimiter>(provider => provider.GetRequiredService<GameLoginTicketRedemptionAttemptLimiter>());

        services.AddSingleton<GameLoginTicketAuthenticationKeyRing>(_ => GameLoginTicketAuthenticationKeyRingFactory.Create(activeVerificationKeyId, verificationKeys));
        services.AddSingleton<IGameLoginTicketRedemptionStore>(provider => new GameLoginTicketRedemptionStore(provider.GetRequiredKeyedService<MySqlDataSource>(AccountPersistenceServiceCollectionExtensions.RawMySqlDataSourceKey), provider.GetRequiredService<GameLoginTicketAuthenticationKeyRing>()));

        services.AddSingleton<GameLoginTicketRedeemer>();

        return services;
    }
}
