using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Controls admission of expensive password verification attempts for resolved accounts.
/// </summary>
public interface IAccountAuthenticationAttemptLimiter
{
    /// <summary>
    /// Attempts to begin authentication for an account from a remote address.
    /// </summary>
    bool TryBeginAuthentication(IPAddress remoteAddress, uint accountId, [NotNullWhen(true)] out IAccountAuthenticationAttemptLease? attempt);
}
