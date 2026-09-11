using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace OpenConquer.Infrastructure.Security.Accounts.Authentication;

internal sealed class AccountLoginConnectionProtection(AccountLoginConnectionProtectionOptions options)
    : IAccountLoginConnectionLimiter
{
    private readonly AccountLoginConnectionProtectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly Lock _gate = new();
    private readonly Dictionary<RemoteAddressSourceKey, int> _activeConnectionsBySource = [];

    public bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);

        RemoteAddressSourceKey sourceKey = RemoteAddressSourceKey.Create(remoteAddress);

        lock (_gate)
        {
            _activeConnectionsBySource.TryGetValue(sourceKey, out int activeConnections);

            if (activeConnections >= _options.MaximumConcurrentConnectionsPerSource)
            {
                connection = null;
                return false;
            }

            _activeConnectionsBySource[sourceKey] = activeConnections + 1;
            connection = new ConnectionLease(this, sourceKey);
            return true;
        }
    }

    private void ReleaseConnection(RemoteAddressSourceKey sourceKey)
    {
        lock (_gate)
        {
            if (!_activeConnectionsBySource.TryGetValue(sourceKey, out int activeConnections) || activeConnections < 1)
            {
                throw new InvalidOperationException("The login-connection admission state is inconsistent.");
            }

            if (activeConnections == 1)
            {
                _activeConnectionsBySource.Remove(sourceKey);
                return;
            }

            _activeConnectionsBySource[sourceKey] = activeConnections - 1;
        }
    }

    private sealed class ConnectionLease(AccountLoginConnectionProtection owner, RemoteAddressSourceKey sourceKey)
        : IAccountLoginConnectionLease
    {
        private const int Active = 0;
        private const int Disposed = 1;

        private int _state;

        public void Dispose()
        {
            int previousState = Interlocked.Exchange(ref _state, Disposed);

            if (previousState == Active)
            {
                owner.ReleaseConnection(sourceKey);
            }
        }
    }
}
