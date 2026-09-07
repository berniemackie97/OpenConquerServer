using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicket
{
    public GameLoginTicket(uint accountId, string username, uint sessionUid, uint authenticationKey, DateTimeOffset issuedAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A game-login ticket must identify a persisted account.");
        }

        if (!AccountCredentialPolicy.IsCanonicalUsername(username))
        {
            throw new ArgumentException("A game-login ticket requires a canonical account username.", nameof(username));
        }

        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A game-login ticket requires a nonzero session UID.");
        }

        if (authenticationKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationKey), "A game-login ticket requires a nonzero authentication key.");
        }

        if (issuedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The game-login ticket issue time must use the UTC offset.", nameof(issuedAtUtc));
        }

        if (expiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The game-login ticket expiration time must use the UTC offset.", nameof(expiresAtUtc));
        }

        if (expiresAtUtc <= issuedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "The game-login ticket expiration time must be later than its issue time.");
        }

        AccountId = accountId;
        Username = username;
        SessionUid = sessionUid;
        AuthenticationKey = authenticationKey;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public uint AccountId { get; }
    public string Username { get; }
    public uint SessionUid { get; }
    public uint AuthenticationKey { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
}
