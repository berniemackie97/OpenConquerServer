using System.Net;
using System.Net.Sockets;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Owns one TCP listening socket and transfers accepted connections into the transport connection abstraction.
/// </summary>
public sealed class SocketTransportListener : IAsyncDisposable
{
    private readonly Socket _socket;
    private int _disposeState;

    public SocketTransportListener(IPEndPoint localEndPoint, int backlog)
    {
        ArgumentNullException.ThrowIfNull(localEndPoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(backlog, 1);

        Socket socket = new(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            socket.Bind(localEndPoint);
            socket.Listen(backlog);

            if (socket.LocalEndPoint is not IPEndPoint boundEndPoint)
            {
                throw new InvalidOperationException("TCP listener did not expose an IP local endpoint.");
            }

            _socket = socket;
            LocalEndPoint = boundEndPoint;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public IPEndPoint LocalEndPoint { get; }

    public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Socket acceptedSocket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            acceptedSocket.NoDelay = true;

            return new SocketTransportConnection(acceptedSocket);
        }
        catch
        {
            acceptedSocket.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _socket.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
    }
}
