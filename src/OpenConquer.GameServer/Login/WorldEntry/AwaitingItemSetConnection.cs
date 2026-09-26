using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.GameServer.Login.Authentication;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an authenticated existing character connection after the client applies map state and before item set hydration.
/// </summary>
internal sealed class AwaitingItemSetConnection : IAsyncDisposable
{
    private AuthenticatedGameConnection? _connection;

    public AwaitingItemSetConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile, GameMapEntryDefinition map)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(map);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The item-set connection profile belongs to a different authenticated account.", nameof(profile));
        }

        if (profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The item-set connection map does not match the character's persisted map.", nameof(map));
        }

        _connection = connection;
        Profile = profile;
        Map = map;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }

    public AuthenticatedGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The item-set connection has already been transferred or disposed.");
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
