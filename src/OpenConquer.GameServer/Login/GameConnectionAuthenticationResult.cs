namespace OpenConquer.GameServer.Login;

internal enum GameConnectionAuthenticationStatus
{
    PeerClosed = 0,
    AuthorizationRejected,
    Authenticated,
}

internal readonly record struct GameConnectionAuthenticationResult
{
    private GameConnectionAuthenticationResult(GameConnectionAuthenticationStatus status, AuthenticatedGameConnection? connection)
    {
        Status = status;
        Connection = connection;
    }

    public GameConnectionAuthenticationStatus Status { get; }
    public AuthenticatedGameConnection? Connection { get; }

    public static GameConnectionAuthenticationResult PeerClosed() => new(GameConnectionAuthenticationStatus.PeerClosed, null);
    public static GameConnectionAuthenticationResult AuthorizationRejected() => new(GameConnectionAuthenticationStatus.AuthorizationRejected, null);

    public static GameConnectionAuthenticationResult Authenticated(AuthenticatedGameConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new GameConnectionAuthenticationResult(GameConnectionAuthenticationStatus.Authenticated, connection);
    }
}
