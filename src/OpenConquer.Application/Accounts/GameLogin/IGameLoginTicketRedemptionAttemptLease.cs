namespace OpenConquer.Application.Accounts.GameLogin;

public interface IGameLoginTicketRedemptionAttemptLease : IDisposable
{
    void Complete(bool authorizationAccepted);
}
