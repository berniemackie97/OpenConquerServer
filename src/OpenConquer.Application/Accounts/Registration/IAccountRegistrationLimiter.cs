using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace OpenConquer.Application.Accounts.Registration;

public interface IAccountRegistrationLimiter
{
    /// <summary>
    /// Admits one registration attempt from <paramref name="remoteAddress"/>. Disposing the lease ends its
    /// concurrency slot; the attempt still counts against the source's quota.
    /// </summary>
    bool TryBeginRegistration(IPAddress remoteAddress, [NotNullWhen(true)] out IDisposable? lease);
}
