using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Authenticates account credentials against authoritative persisted account
/// state.
/// </summary>
public interface IAccountAuthenticator
{
    /// <summary>
    /// Authenticates <paramref name="accountName"/> using caller-owned password
    /// memory.
    /// </summary>
    /// <remarks>
    /// The caller must keep <paramref name="password"/> valid and unchanged
    /// until the returned operation completes. Implementations must not retain
    /// the supplied memory after completion.
    /// </remarks>
    ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default);
}
