using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Items;
using OpenConquer.Infrastructure.Persistence.Game.Login;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Social;

namespace OpenConquer.Infrastructure.Persistence.Game.Extensions;

public static class GamePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString)
    {
        return AddGamePersistence(services, connectionString, new CharacterItemHydrationOptions(), new CharacterSocialRelationHydrationOptions());
    }

    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString, CharacterItemHydrationOptions itemHydrationOptions)
    {
        return AddGamePersistence(services, connectionString, itemHydrationOptions, new CharacterSocialRelationHydrationOptions());
    }

    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString, CharacterItemHydrationOptions itemHydrationOptions, CharacterSocialRelationHydrationOptions socialRelationHydrationOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(itemHydrationOptions);
        ArgumentNullException.ThrowIfNull(socialRelationHydrationOptions);

        services.AddPooledDbContextFactory<GameDbContext>(options => GameDbContextOptionsConfiguration.Configure(options, connectionString));
        services.AddSingleton(itemHydrationOptions);
        services.AddSingleton(socialRelationHydrationOptions);
        services.AddSingleton<ICharacterLoginProfileRepository, CharacterLoginProfileRepository>();
        services.AddSingleton<ICharacterItemSetRepository, CharacterItemSetRepository>();
        services.AddSingleton<ICharacterSocialRelationSetRepository, CharacterSocialRelationSetRepository>();
        services.AddSingleton<GameDatabaseReadinessVerifier>();
        services.AddSingleton<IGameDatabaseReadinessVerifier>(provider => provider.GetRequiredService<GameDatabaseReadinessVerifier>());

        return services;
    }
}
