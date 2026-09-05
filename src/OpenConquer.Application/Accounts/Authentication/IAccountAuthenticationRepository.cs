namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Provides the persistence operations required by account authentication.
/// </summary>
public interface IAccountAuthenticationRepository
{
    /// <summary>
    /// Finds the persisted authentication state for a validated, trimmed account name.
    /// Matching case/collation semantics belong to the persistence adapter.
    /// Each call must read current authoritative state, including when revalidating
    /// after a failed conditional login update.
    /// </summary>
    ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records the login timestamp and optional password-hash migration if
    /// the account identity, username, and password hash still match the snapshot and
    /// its current permission allows login. Comparisons of hashes must be byte-exact.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a matching account was updated, including an
    /// identical timestamp; otherwise <see langword="false"/>. Null replacement retains
    /// the password hash. A false result requires credential and access revalidation.
    /// </returns>
    ValueTask<bool> TryRecordLoginAsync(AccountAuthenticationSnapshot account, string? replacementPasswordHash,
        uint loginTimestamp, CancellationToken cancellationToken = default);
}
