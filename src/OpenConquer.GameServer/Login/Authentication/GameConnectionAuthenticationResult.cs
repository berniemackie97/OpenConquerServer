namespace OpenConquer.GameServer.Login.Authentication;

internal enum GameConnectionAuthenticationStatus
{
    PeerClosed = 0,
    AuthorizationRejected,
    Authenticated,
}

internal sealed class GameConnectionAuthenticationResult : IAsyncDisposable
{
    private AuthenticatedGameConnection? _connection;

    private GameConnectionAuthenticationResult(GameConnectionAuthenticationStatus status, AuthenticatedGameConnection? connection)
    {
        Status = status;
        _connection = connection;
    }

    public GameConnectionAuthenticationStatus Status { get; }

    public AuthenticatedGameConnection TakeConnection()
    {
        if (Status != GameConnectionAuthenticationStatus.Authenticated)
        {
            throw new InvalidOperationException("Only an authenticated GameServer result owns a live connection.");
        }

        return Interlocked.Exchange(ref _connection, null)
               ?? throw new InvalidOperationException("The authenticated GameServer connection has already been transferred or disposed.");
    }

    public async ValueTask DisposeAsync()
    {
        AuthenticatedGameConnection? connection = Interlocked.Exchange(ref _connection, null);

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static GameConnectionAuthenticationResult PeerClosed() => new(GameConnectionAuthenticationStatus.PeerClosed, null);
    public static GameConnectionAuthenticationResult AuthorizationRejected() => new(GameConnectionAuthenticationStatus.AuthorizationRejected, null);

    public static GameConnectionAuthenticationResult Authenticated(AuthenticatedGameConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new GameConnectionAuthenticationResult(GameConnectionAuthenticationStatus.Authenticated, connection);
    }
}
