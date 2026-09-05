using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Authenticates account credentials.
/// </summary>
public interface IAccountAuthenticator
{
    /// <summary>
    /// Authenticates <paramref name="accountName"/>
    /// </summary>
    ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default);
}
