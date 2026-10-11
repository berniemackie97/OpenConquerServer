using System.Net;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Login.Admission;

/// <summary>
/// Limits each source's connection occupancy from TCP acceptance until the owned transport finishes disposal.
/// </summary>
internal sealed class GameAdmissionTransportListener(
    ITransportConnectionListener listener,
    int maximumConnectionsPerSource,
    GameLoginMetrics metrics) : ITransportConnectionListener
{
    private readonly ITransportConnectionListener _listener = listener ?? throw new ArgumentNullException(nameof(listener));
    private readonly GameLoginMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly Lock _gate = new();
    private readonly Dictionary<IPAddress, int> _connections = [];
    private readonly int _maximumConnectionsPerSource = ValidateLimit(maximumConnectionsPerSource);

    public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ITransportConnection connection = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            IPAddress address;
            bool reserved;

            try
            {
                if (connection.RemoteEndPoint is not IPEndPoint remote)
                {
                    throw new InvalidOperationException("The GameServer accepted a transport without an IP remote endpoint.");
                }

                address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
                reserved = TryReserve(address);
            }
            catch (Exception admissionFailure)
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("GameServer source admission and transport cleanup both failed.", admissionFailure, cleanupFailure);
                }

                throw;
            }

            if (!reserved)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                _metrics.RecordSourceRejected();
                continue;
            }

            try
            {
                ITransportConnection admitted = new SourceConnection(this, address, connection);
                _metrics.RecordAccepted();
                return admitted;
            }
            catch (Exception admissionFailure)
            {
                Exception? cleanupFailure = null;

                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }
                finally
                {
                    Release(address);
                }

                if (cleanupFailure is not null)
                {
                    throw new AggregateException("GameServer source admission and transport cleanup both failed.", admissionFailure, cleanupFailure);
                }

                throw;
            }
        }
    }

    public ValueTask DisposeAsync() => _listener.DisposeAsync();

    private bool TryReserve(IPAddress address)
    {
        lock (_gate)
        {
            _connections.TryGetValue(address, out int count);

            if (count >= _maximumConnectionsPerSource)
            {
                return false;
            }

            _connections[address] = count + 1;
            return true;
        }
    }

    private void Release(IPAddress address)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(address, out int count) || count < 1)
            {
                throw new InvalidOperationException("GameServer source connection occupancy is inconsistent.");
            }

            if (count == 1)
            {
                _connections.Remove(address);
            }
            else
            {
                _connections[address] = count - 1;
            }
        }
    }

    private static int ValidateLimit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return limit;
    }

    private sealed class SourceConnection : ITransportConnection
    {
        private readonly GameAdmissionTransportListener _owner;
        private readonly IPAddress _address;
        private readonly ITransportConnection _connection;
        private readonly Lock _gate = new();
        private Task? _disposeTask;

        public SourceConnection(GameAdmissionTransportListener owner, IPAddress address, ITransportConnection connection)
        {
            _owner = owner;
            _address = address;
            _connection = connection;
        }

        public EndPoint LocalEndPoint => _connection.LocalEndPoint;
        public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _connection.ReceiveAsync(buffer, cancellationToken);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _connection.SendAsync(buffer, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                return new ValueTask(_disposeTask ??= DisposeCoreAsync());
            }
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _owner.Release(_address);
            }
        }
    }
}
