namespace OpenConquer.Application.Accounts.GameLogin;

public interface IGameLoginTicketGrantStore
{
    ValueTask<GameLoginTicketGrantResult> TryGrantAsync(GameLoginTicketGrantRequest request, TimeSpan ticketLifetime, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default);
}
