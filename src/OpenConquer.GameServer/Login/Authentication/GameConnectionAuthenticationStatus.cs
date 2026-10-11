namespace OpenConquer.GameServer.Login.Authentication;

internal enum GameConnectionAuthenticationStatus
{
    PeerClosed = 0,
    AuthorizationRejected,
    Authenticated,
}
