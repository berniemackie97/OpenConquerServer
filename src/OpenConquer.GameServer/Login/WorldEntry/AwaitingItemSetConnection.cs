using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after the client applies map state and before item-set hydration.
/// </summary>
internal sealed class AwaitingItemSetConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingItemSetConnection(ExistingCharacterGameConnection connection, GameMapEntryDefinition map)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The item-set connection map does not match the character's persisted map.", nameof(map));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
    }

    public CharacterLoginProfile Profile { get; }
    public GameMapEntryDefinition Map { get; }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The item-set connection has already been transferred or disposed.");
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
