namespace OpenConquer.Application.Accounts.GameLogin.Redemption;

public interface IGameLoginTicketRedemptionStore
{
    ValueTask<GameLoginTicketIdentity?> TryRedeemAsync(uint sessionUid, uint authenticationKey, CancellationToken cancellationToken = default);
}
