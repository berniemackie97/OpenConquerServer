using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.Bootstrap.Maps;

/// <summary>
/// Owns an existing-character connection and its resolved map content before EnterMap.
/// </summary>
internal sealed class AwaitingEnterMapConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingEnterMapConnection(ExistingCharacterGameConnection connection, WorldMapContent map)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(map);

        if (connection.Profile.Location.MapId != map.MapId)
        {
            throw new ArgumentException("The EnterMap connection map does not match the character's persisted map.", nameof(map));
        }

        _connection = connection;
        Profile = connection.Profile;
        Map = map;
    }

    public CharacterLoginProfile Profile
    {
        get;
    }

    public WorldMapContent Map
    {
        get;
    }

    public ExistingCharacterGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null) ?? throw new InvalidOperationException("The EnterMap connection has already been transferred or disposed.");
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
