namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Represents the result of authenticating an account.
/// </summary>
public readonly record struct AccountAuthenticationResult
{
    private AccountAuthenticationResult(AccountAuthenticationStatus status, uint accountId)
    {
        Status = status;
        AccountId = accountId;
    }

    public AccountAuthenticationStatus Status { get; }

    /// <summary>
    /// Gets the authenticated account identifier when
    /// <see cref="Status"/> is <see cref="AccountAuthenticationStatus.Success"/>.
    /// </summary>
    public uint AccountId { get; }

    public bool IsSuccess => Status == AccountAuthenticationStatus.Success;

    public static AccountAuthenticationResult Succeeded(uint accountId)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A successful authentication result must identify a persisted account.");
        }

        return new AccountAuthenticationResult(AccountAuthenticationStatus.Success, accountId);
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
