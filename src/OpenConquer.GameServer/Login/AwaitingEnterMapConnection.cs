using OpenConquer.Application.Characters.Login;
using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Owns an authenticated existing-character connection after bootstrap completion and before the client enters the world.
/// </summary>
internal sealed class AwaitingEnterMapConnection : IAsyncDisposable
{
    private AuthenticatedGameConnection? _connection;

    public AwaitingEnterMapConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The bootstrapped character profile belongs to a different authenticated account.", nameof(profile));
        }

        _connection = connection;
        Profile = profile;
    }

    public CharacterLoginProfile Profile { get; }

    public AuthenticatedGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null)
               ?? throw new InvalidOperationException("The EnterMap connection has already been transferred or disposed.");
    }

    public async ValueTask DisposeAsync()
    {
        AuthenticatedGameConnection? connection = Interlocked.Exchange(ref _connection, null);

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
