using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Infrastructure.Persistence.Game.Context;
using OpenConquer.Infrastructure.Persistence.Game.Login;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.Infrastructure.Persistence.Game.Extensions;

public static class GamePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddPooledDbContextFactory<GameDbContext>(options => GameDbContextOptionsConfiguration.Configure(options, connectionString));
        services.AddSingleton<ICharacterLoginProfileRepository, CharacterLoginProfileRepository>();
        services.AddSingleton<GameDatabaseReadinessVerifier>();
        services.AddSingleton<IGameDatabaseReadinessVerifier>(provider => provider.GetRequiredService<GameDatabaseReadinessVerifier>());

        return services;
    }
}
