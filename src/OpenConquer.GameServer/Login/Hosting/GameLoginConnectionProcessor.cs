using OpenConquer.Application.Characters.Login.Resolution;
using OpenConquer.GameServer.Login.Authentication;
using OpenConquer.GameServer.Login.Character.Resolution;
using OpenConquer.GameServer.World.Sessions;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Login.Hosting;

/// <summary>
/// Authenticates an admitted transport, resolves its character route, and transfers existing characters into the world.
/// </summary>
internal sealed class GameLoginConnectionProcessor(GameConnectionHandoffProcessor authentication, CharacterLoginHandoffProcessor characterResolution, GameExistingCharacterLoginProcessor existingCharacter)
{
    private readonly GameConnectionHandoffProcessor _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly CharacterLoginHandoffProcessor _characterResolution = characterResolution ?? throw new ArgumentNullException(nameof(characterResolution));
    private readonly GameExistingCharacterLoginProcessor _existingCharacter = existingCharacter ?? throw new ArgumentNullException(nameof(existingCharacter));

    public async ValueTask<WorldSession?> ProcessAsync(ITransportConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using GameConnectionAuthenticationResult authentication = await _authentication.ProcessAsync(connection, cancellationToken).ConfigureAwait(false);

        if (authentication.Status != GameConnectionAuthenticationStatus.Authenticated)
        {
            return null;
        }

        await using CharacterLoginHandoffResult character = await _characterResolution.ProcessAsync(authentication, cancellationToken).ConfigureAwait(false);

        if (character.Resolution.Route == CharacterLoginRoute.CharacterCreation)
        {
            return null;
        }

        if (character.Resolution.Route != CharacterLoginRoute.ExistingCharacter)
        {
            throw new InvalidOperationException($"Unsupported GameServer character-login route '{character.Resolution.Route}'.");
        }

        return await _existingCharacter.ProcessAsync(character, cancellationToken).ConfigureAwait(false);
    }
}
