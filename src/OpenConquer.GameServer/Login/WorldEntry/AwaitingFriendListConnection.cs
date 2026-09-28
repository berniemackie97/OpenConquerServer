using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.GameServer.Login.Authentication;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an authenticated existing-character connection after item-set hydration and before the friend-list bootstrap rung.
/// </summary>
internal sealed class AwaitingFriendListConnection : IAsyncDisposable
{
    private AuthenticatedGameConnection? _connection;

    public AwaitingFriendListConnection(AuthenticatedGameConnection connection, CharacterLoginProfile profile, GameMapEntryDefinition map, CharacterItemSet itemSet)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(itemSet);

        if (profile.Identity.AccountId != connection.AccountId)
        {
            throw new ArgumentException("The friend-list connection profile belongs to a different authenticated account.", nameof(profile));
        }

        if (profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The friend-list connection map does not match the character's persisted map.", nameof(map));
        }

        if (itemSet.CharacterId != profile.Identity.CharacterId)
        {
            throw new ArgumentException("The friend-list connection item set belongs to a different character.", nameof(itemSet));
        }

        _connection = connection;
        Profile = profile;
        Map = map;
        ItemSet = itemSet;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }
    public CharacterItemSet ItemSet { get; }

    public AuthenticatedGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null)
               ?? throw new InvalidOperationException("The friend-list connection has already been transferred or disposed.");
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
