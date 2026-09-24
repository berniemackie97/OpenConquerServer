using OpenConquer.Application.Characters.Login;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Represents an authenticated existing character connection after bootstrap completion and before the client enters the world.
/// </summary>
internal sealed class AwaitingEnterMapConnection
{
    public AwaitingEnterMapConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The bootstrapped character profile belongs to a different authenticated account.", nameof(profile));
        }

        Connection = connection;
        Profile = profile;
    }

    public AuthenticatedGameConnection Connection { get; }
    public CharacterLoginProfile Profile { get; }
}
