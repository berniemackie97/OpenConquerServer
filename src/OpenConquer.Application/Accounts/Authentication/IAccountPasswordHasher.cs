namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Provides password hashing and verification.
/// </summary>
public interface IAccountPasswordHasher
{
    /// <summary>
    /// Verifies a supplied password against a persisted encoded hash.
    /// </summary>
    AccountPasswordVerificationStatus VerifyPassword(string passwordHash, ReadOnlySpan<char> password);

    /// <summary>
    /// Performs equivalent password verification work for an account lookup miss so account existence cannot be inferred
    /// </summary>
    void VerifyDecoy(ReadOnlySpan<char> password);

    /// <summary>
    /// Creates the currently preferred encoded password hash.
    /// </summary>
    string HashPassword(ReadOnlySpan<char> password);
}
