using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Creation;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.DependencyInjection;

public static class GameCharacterCreationPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGameCharacterCreationPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<ICharacterCreationStore>(_ => new CharacterCreationStore(connectionString));

        return services;
    }
}
