using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Items;
using OpenConquer.Infrastructure.Persistence.Game.Login;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.Infrastructure.Persistence.Game.Extensions;

public static class GamePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString)
    {
        return AddGamePersistence(services, connectionString, new CharacterItemHydrationOptions());
    }

    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString, CharacterItemHydrationOptions itemHydrationOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(itemHydrationOptions);

        services.AddPooledDbContextFactory<GameDbContext>(options => GameDbContextOptionsConfiguration.Configure(options, connectionString));
        services.AddSingleton(itemHydrationOptions);
        services.AddSingleton<ICharacterLoginProfileRepository, CharacterLoginProfileRepository>();
        services.AddSingleton<ICharacterItemSetRepository, CharacterItemSetRepository>();
        services.AddSingleton<GameDatabaseReadinessVerifier>();
        services.AddSingleton<IGameDatabaseReadinessVerifier>(provider => provider.GetRequiredService<GameDatabaseReadinessVerifier>());

        return services;
    }
}
