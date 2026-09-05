namespace OpenConquer.Application.Accounts.Authentication;

public interface IAccountAuthenticationRepository
{
    /// <summary>
    /// Finds the persisted authentication state for a validated, trimmed account name.
    /// </summary>
    ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the login timestamp and optional password hash migration.
    /// </summary>
    ValueTask<bool> TryRecordLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash, uint loginTimestamp, CancellationToken cancellationToken = default);
}
