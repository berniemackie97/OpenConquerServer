using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketIdentity
{
    public GameLoginTicketIdentity(uint accountId, string username, uint sessionUid)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A redeemed game-login identity must identify a persisted account.");
        }

        if (!AccountCredentialPolicy.IsCanonicalUsername(username))
        {
            throw new ArgumentException("A redeemed game-login identity requires a canonical account username.", nameof(username));
        }

        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A redeemed game-login identity requires a nonzero session UID.");
        }

        AccountId = accountId;
        Username = username;
        SessionUid = sessionUid;
    }

    public uint AccountId { get; }
    public string Username { get; }
    public uint SessionUid { get; }
}
