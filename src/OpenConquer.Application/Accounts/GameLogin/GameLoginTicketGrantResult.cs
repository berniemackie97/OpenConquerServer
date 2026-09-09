namespace OpenConquer.Application.Accounts.GameLogin;

public readonly record struct GameLoginTicketGrantResult
{
    private GameLoginTicketGrantResult(GameLoginTicketGrantStatus status, GameLoginTicket? ticket)
    {
        Status = status;
        Ticket = ticket;
    }

    public GameLoginTicketGrantStatus Status { get; }
    public GameLoginTicket? Ticket { get; }

    public bool IsGranted => Status == GameLoginTicketGrantStatus.Granted;

    public static GameLoginTicketGrantResult Granted(GameLoginTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return new GameLoginTicketGrantResult(GameLoginTicketGrantStatus.Granted, ticket);
    }

    public static GameLoginTicketGrantResult SessionUidCollision()
    {
        return new GameLoginTicketGrantResult(GameLoginTicketGrantStatus.SessionUidCollision, ticket: null);
    }

    public static GameLoginTicketGrantResult AuthenticationStateChanged()
    {
        return new GameLoginTicketGrantResult(GameLoginTicketGrantStatus.AuthenticationStateChanged, ticket: null);
    }
}
