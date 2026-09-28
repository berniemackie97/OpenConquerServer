namespace OpenConquer.Application.Accounts.GameLogin.Redemption;

public interface IGameLoginTicketRedemptionAttemptLease : IDisposable
{
    void Complete(bool authorizationAccepted);
}
