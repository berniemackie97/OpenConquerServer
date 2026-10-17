using Microsoft.Extensions.DependencyInjection;
using OpenConquer.Application.Characters.Location;
using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.DependencyInjection;

/// <summary>
/// Opt-in persistence registration for a dedicated character-location writer credential.
/// The ordinary GameServer read-only service registration remains unchanged.
/// </summary>
public static class GameCharacterLocationWriteServiceCollectionExtensions
{
    public static IServiceCollection AddGameCharacterLocationWrite(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<ICharacterLocationWriteStore>(_ => new CharacterLocationWriteStore(connectionString));
        return services;
    }
}
