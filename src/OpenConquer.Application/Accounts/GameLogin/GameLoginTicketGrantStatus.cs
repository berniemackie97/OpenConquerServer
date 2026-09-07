namespace OpenConquer.Application.Accounts.GameLogin;

public enum GameLoginTicketGrantStatus
{
    Granted = 1,
    SessionUidCollision = 2,
    AuthenticationStateChanged = 3,
}
