using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Checks account credentials against the stored account.
/// </summary>
public interface IAccountAuthenticator
{
    /// <summary>
    /// Authenticates <paramref name="accountName"/> with <paramref name="password"/>.
    /// </summary>
    /// <remarks>
    /// Account names are trimmed and must be 1 to 32 characters. Passwords must be 1 to 128 characters and are used
    /// as given. Invalid credentials are rejected before any database or hashing work. A null account name is a
    /// programming error. Keep <paramref name="password"/> unchanged until the returned task completes; implementations
    /// don't hold on to it afterwards.
    /// </remarks>
    ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default);
}
