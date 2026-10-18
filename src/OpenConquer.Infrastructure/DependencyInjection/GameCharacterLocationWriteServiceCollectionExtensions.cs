using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.DependencyInjection;

/// <summary>
/// Opt-in persistence registration for one dedicated, column-restricted character-location
/// credential shared by the CAS writer and identity-scoped location observer.
/// The ordinary GameServer read-only service registration remains unchanged.
/// </summary>
public static class GameCharacterLocationWriteServiceCollectionExtensions
{
    public static IServiceCollection AddGameCharacterLocationWrite(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ => new CharacterLocationWriteStore(connectionString));
        services.AddSingleton<ICharacterLocationWriteStore>(provider => provider.GetRequiredService<CharacterLocationWriteStore>());
        services.AddSingleton<ICharacterLocationObservationStore>(provider => provider.GetRequiredService<CharacterLocationWriteStore>());
        return services;
    }
}
