namespace OpenConquer.Application.Accounts.GameLogin;

public interface IGameLoginTicketGrantStore
{
    ValueTask<GameLoginTicketGrantStatus> TryGrantAsync(GameLoginTicket ticket, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default);
}
