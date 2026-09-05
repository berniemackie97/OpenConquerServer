namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents the result of authenticating an account.
/// </summary>
public readonly record struct AccountAuthenticationResult
{
    private AccountAuthenticationResult(AccountAuthenticationStatus status, uint accountId, string? username = null)
    {
        Status = status;
        AccountId = accountId;
        Username = username;
    }

    public AccountAuthenticationStatus Status { get; }

    /// <summary>
    /// Gets the authenticated account identifier when
    /// <see cref="Status"/> is <see cref="AccountAuthenticationStatus.Success"/>.
    /// </summary>
    public uint AccountId { get; }

    /// <summary>Gets the canonical persisted username on success; otherwise null.</summary>
    public string? Username { get; }

    public bool IsSuccess => Status == AccountAuthenticationStatus.Success;

    public static AccountAuthenticationResult Succeeded(uint accountId, string username)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A successful authentication result must identify a persisted account.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return new AccountAuthenticationResult(AccountAuthenticationStatus.Success, accountId, username);
    }

    public static AccountAuthenticationResult InvalidCredentials()
    {
        return new AccountAuthenticationResult(AccountAuthenticationStatus.InvalidCredentials, accountId: 0);
    }

    public static AccountAuthenticationResult Banned()
    {
        return new AccountAuthenticationResult(AccountAuthenticationStatus.Banned, accountId: 0);
    }
}
