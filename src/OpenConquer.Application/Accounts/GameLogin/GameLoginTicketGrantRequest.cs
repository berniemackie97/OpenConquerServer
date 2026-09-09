using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketGrantRequest
{
    public GameLoginTicketGrantRequest(uint accountId, string username, uint sessionUid, uint authenticationKey)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A game-login ticket grant must identify a persisted account.");
        }

        if (!AccountCredentialPolicy.IsCanonicalUsername(username))
        {
            throw new ArgumentException("A game-login ticket grant requires a canonical account username.", nameof(username));
        }

        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A game-login ticket grant requires a nonzero session UID.");
        }

        if (authenticationKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationKey), "A game-login ticket grant requires a nonzero authentication key.");
        }

        AccountId = accountId;
        Username = username;
        SessionUid = sessionUid;
        AuthenticationKey = authenticationKey;
    }

    public uint AccountId { get; }
    public string Username { get; }
    public uint SessionUid { get; }
    public uint AuthenticationKey { get; }
}
