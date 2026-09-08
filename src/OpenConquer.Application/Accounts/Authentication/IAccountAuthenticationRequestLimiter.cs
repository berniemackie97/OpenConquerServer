using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Controls admission of account-authentication requests before account resolution
/// and password verification.
/// </summary>
public interface IAccountAuthenticationRequestLimiter
{
    /// <summary>
    /// Attempts to admit an authentication request from a remote address.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the request is admitted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    bool TryBeginAuthentication(
        IPAddress remoteAddress,
        [NotNullWhen(true)] out IAccountAuthenticationRequestLease? request
    );
}
