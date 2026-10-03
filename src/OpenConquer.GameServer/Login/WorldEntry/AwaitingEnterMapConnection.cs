using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.GameServer.Login.Character;

namespace OpenConquer.GameServer.Login.WorldEntry;

/// <summary>
/// Owns an existing-character connection after bootstrap completion and before the client enters the world.
/// </summary>
internal sealed class AwaitingEnterMapConnection : IAsyncDisposable
{
    private ExistingCharacterGameConnection? _connection;

    public AwaitingEnterMapConnection(ExistingCharacterGameConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        Profile = connection.Profile;
    }

    public CharacterLoginProfile Profile { get; }

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
