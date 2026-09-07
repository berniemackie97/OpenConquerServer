namespace OpenConquer.Application.Accounts.GameLogin;

public interface IGameLoginTicketRedemptionStore
{
    ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default);
}
