using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the character-location CAS writer, observer, fence and permission probe over the GameServer's Game
/// database identity, whose <c>characters</c> updates are limited to the location and fence columns.
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
        services.AddSingleton<ICharacterLocationFenceStore>(provider => provider.GetRequiredService<CharacterLocationWriteStore>());
        services.AddSingleton<ICharacterLocationWriterReadinessVerifier>(provider => provider.GetRequiredService<CharacterLocationWriteStore>());
        return services;
    }
}
