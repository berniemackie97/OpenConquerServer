using OpenConquer.Application.Characters.Login;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Owns an authenticated GameServer connection together with the persisted
/// character-login route resolved for its canonical account identity.
/// </summary>
internal sealed class CharacterLoginHandoffResult : IAsyncDisposable
{
    private AuthenticatedGameConnection? _connection;

    public CharacterLoginHandoffResult(AuthenticatedGameConnection connection, CharacterLoginResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (resolution.Route == CharacterLoginRoute.Unspecified)
        {
            throw new ArgumentException("Character login handoff requires a resolved character-login route.", nameof(resolution));
        }

        if (resolution.Route == CharacterLoginRoute.ExistingCharacter)
        {
            CharacterLoginProfile profile = resolution.Profile
                                            ?? throw new ArgumentException("An existing-character handoff requires a character profile.", nameof(resolution));

            if (profile.Identity.AccountId != connection.AccountId)
            {
                throw new ArgumentException("The resolved character profile belongs to a different authenticated account.", nameof(resolution));
            }
        }
        else if (resolution.Route != CharacterLoginRoute.CharacterCreation)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution.Route, "The character-login route is unsupported.");
        }

        _connection = connection;
        Resolution = resolution;
    }

    public CharacterLoginResolution Resolution { get; }

    public AuthenticatedGameConnection TakeConnection()
    {
        return Interlocked.Exchange(ref _connection, null)
               ?? throw new InvalidOperationException("The character-login handoff connection has already been transferred or disposed.");
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
