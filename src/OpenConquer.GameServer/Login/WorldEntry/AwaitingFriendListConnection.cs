using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.Items.Hydration;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after item-set hydration and before the friend-list bootstrap rung.
/// </summary>
internal sealed class AwaitingFriendListConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingFriendListConnection(ExistingCharacterGameConnection connection, GameMapEntryDefinition map, CharacterItemSet itemSet)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(itemSet);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The friend-list connection map does not match the character's persisted map.", nameof(map));
        }

        if (itemSet.CharacterId != connection.Profile.Identity.CharacterId)
        {
            throw new ArgumentException("The friend-list connection item set belongs to a different character.", nameof(itemSet));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
        ItemSet = itemSet;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }
    public CharacterItemSet ItemSet { get; }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The friend-list connection has already been transferred or disposed.");
    }

    public async ValueTask DisposeAsync()
    {
        ExistingCharacterGameConnection? connection = Interlocked.Exchange(ref _connection, null);

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
