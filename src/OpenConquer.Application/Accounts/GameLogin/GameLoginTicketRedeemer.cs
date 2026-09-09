using System.Net;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketRedeemer(IGameLoginTicketRedemptionStore redemptionStore, IGameLoginTicketRedemptionAttemptLimiter attemptLimiter)
{
    private readonly IGameLoginTicketRedemptionStore _redemptionStore = redemptionStore ?? throw new ArgumentNullException(nameof(redemptionStore));
    private readonly IGameLoginTicketRedemptionAttemptLimiter _attemptLimiter = attemptLimiter ?? throw new ArgumentNullException(nameof(attemptLimiter));

    public async ValueTask<GameLoginTicketIdentity?> RedeemAsync(uint sessionUid, uint authenticationKey, IPAddress remoteAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        cancellationToken.ThrowIfCancellationRequested();

        if (sessionUid == 0 || authenticationKey == 0)
        {
            return null;
        }

        if (!_attemptLimiter.TryBeginRedemption(remoteAddress, sessionUid, out IGameLoginTicketRedemptionAttemptLease? redemptionAttempt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        using (redemptionAttempt)
        {
            GameLoginTicketIdentity? identity = await _redemptionStore.TryRedeemAsync(sessionUid, authenticationKey, cancellationToken).ConfigureAwait(false);

            if (identity is null)
            {
                redemptionAttempt.Complete(authorizationAccepted: false);
                return null;
            }

            if (identity.SessionUid != sessionUid)
            {
                throw new InvalidOperationException("Game-login ticket persistence returned an identity for a different session UID.");
            }

            redemptionAttempt.Complete(authorizationAccepted: true);

            return identity;
        }
    }
}
