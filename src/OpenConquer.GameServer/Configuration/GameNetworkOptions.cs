using System.Net;

namespace OpenConquer.GameServer.Configuration;

internal sealed class GameNetworkOptions
{
    public GameNetworkOptions(IPEndPoint endpoint, int listenBacklog, int admissionCapacity, int maximumConnectionsPerSource, int workerCount, TimeSpan connectionTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || endpoint.Port is < 1 or > IPEndPoint.MaxPort)
        {
            throw new ArgumentException("GameServer network binding requires a valid IPv4 address and nonzero TCP port.", nameof(endpoint));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(listenBacklog, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(listenBacklog, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(admissionCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(admissionCapacity, 65536);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConnectionsPerSource, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumConnectionsPerSource, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerCount, 1024);

        if (connectionTimeout <= TimeSpan.Zero || connectionTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout), "GameServer login processing timeout must be between zero and ten minutes, exclusive of zero.");
        }

        EndPoint = new IPEndPoint(endpoint.Address, endpoint.Port);
        ListenBacklog = listenBacklog;
        AdmissionCapacity = admissionCapacity;
        MaximumConnectionsPerSource = maximumConnectionsPerSource;
        WorkerCount = workerCount;
        ConnectionTimeout = connectionTimeout;
    }

    public IPEndPoint EndPoint
    {
        get;
    }

    public int ListenBacklog
    {
        get;
    }

    public int AdmissionCapacity
    {
        get;
    }

    public int MaximumConnectionsPerSource
    {
        get;
    }

    public int WorkerCount
    {
        get;
    }

    public TimeSpan ConnectionTimeout
    {
        get;
    }
}
