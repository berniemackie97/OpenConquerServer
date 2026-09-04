namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Provides the persistence operations required by account authentication.
/// </summary>
public interface IAccountAuthenticationRepository
{
    /// <summary>
    /// Finds the persisted authentication state for an account name.
    /// </summary>
    ValueTask<AccountAuthenticationSnapshot?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimistically replaces an obsolete password hash after successful
    /// authentication.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the expected hash was still current and was
    /// replaced; otherwise <see langword="false"/>.
    /// </returns>
    ValueTask<bool> TryReplacePasswordHashAsync(uint accountId, string expectedPasswordHash, string replacementPasswordHash, CancellationToken cancellationToken = default);
}
