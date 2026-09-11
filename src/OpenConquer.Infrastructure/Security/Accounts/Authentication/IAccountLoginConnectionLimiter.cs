using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

/// <summary>
/// Controls per-source admission of AccountServer login connections before authentication begins.
/// </summary>
public interface IAccountLoginConnectionLimiter
{
    /// <summary>
    /// Attempts to admit a login connection from a remote address.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the connection is admitted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection);
}
