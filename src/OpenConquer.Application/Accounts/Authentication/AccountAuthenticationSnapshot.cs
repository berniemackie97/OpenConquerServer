namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Contains the minimal persisted account state required to authenticate one
/// login attempt.
/// </summary>
public sealed class AccountAuthenticationSnapshot
{
    public AccountAuthenticationSnapshot(uint accountId, string passwordHash, AccountLoginAccess access)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A persisted authentication snapshot must identify an account.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "The account login access value is not supported.");
        }

        AccountId = accountId;
        PasswordHash = passwordHash;
        Access = access;
    }

    public uint AccountId { get; }
    public string PasswordHash { get; }
    public AccountLoginAccess Access { get; }
}
