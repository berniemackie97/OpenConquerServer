using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents the result of authenticating an account.
/// </summary>
public readonly record struct AccountAuthenticationResult
{
    private AccountAuthenticationResult(AccountAuthenticationStatus status, uint accountId, string? username, ulong accountStateRevision, ulong passwordCredentialRevision)
    {
        Status = status;
        AccountId = accountId;
        Username = username;
        AccountStateRevision = accountStateRevision;
        PasswordCredentialRevision = passwordCredentialRevision;
    }

    public AccountAuthenticationStatus Status { get; }
    public uint AccountId { get; }
    public string? Username { get; }
    public ulong AccountStateRevision { get; }
    public ulong PasswordCredentialRevision { get; }

    public bool IsSuccess => Status == AccountAuthenticationStatus.Success;

    public static AccountAuthenticationResult Succeeded(uint accountId, string username, ulong accountStateRevision, ulong passwordCredentialRevision)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A successful authentication result must identify a persisted account.");
        }

        if (!AccountCredentialPolicy.IsCanonicalUsername(username))
        {
            throw new ArgumentException("A successful authentication result requires a canonical account username.", nameof(username));
        }

        if (accountStateRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountStateRevision), "A successful authentication result requires a nonzero account-state revision.");
        }

        if (passwordCredentialRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(passwordCredentialRevision), "A successful authentication result requires a nonzero password-credential revision.");
        }

        return new AccountAuthenticationResult(AccountAuthenticationStatus.Success, accountId, username, accountStateRevision, passwordCredentialRevision);
    }

    public static AccountAuthenticationResult InvalidCredentials()
    {
        return new AccountAuthenticationResult(AccountAuthenticationStatus.InvalidCredentials, accountId: 0, username: null, accountStateRevision: 0, passwordCredentialRevision: 0);
    }

    public static AccountAuthenticationResult Banned()
    {
        return new AccountAuthenticationResult(AccountAuthenticationStatus.Banned, accountId: 0, username: null, accountStateRevision: 0, passwordCredentialRevision: 0);
    }
}
