using System.Net;
using System.Net.Sockets;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Owns one established stream socket and exposes it through the transport connection contract.
/// </summary>
public sealed class SocketTransportConnection : ITransportConnection
{
    private readonly Socket _socket;
    private int _disposeState;
    private int _receiveActive;
    private int _sendActive;

    public SocketTransportConnection(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        try
        {
            if (socket.SocketType != SocketType.Stream)
            {
                throw new ArgumentException("Transport connections require a stream socket.", nameof(socket));
            }

            EndPoint? localEndPoint = socket.LocalEndPoint;
            EndPoint? remoteEndPoint = socket.RemoteEndPoint;

            if (localEndPoint is null || remoteEndPoint is null)
            {
                throw new ArgumentException("Transport connections require an established socket with local and remote endpoints.", nameof(socket));
            }

            _socket = socket;
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public EndPoint LocalEndPoint { get; }
    public EndPoint RemoteEndPoint { get; }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Interlocked.Exchange(ref _receiveActive, 1) != 0)
        {
            throw new InvalidOperationException("Only one receive operation may be active per transport connection.");
        }

        try
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _receiveActive, 0);
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Interlocked.Exchange(ref _sendActive, 1) != 0)
        {
            throw new InvalidOperationException("Only one send operation may be active per transport connection.");
        }

        try
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            int sent = 0;

            while (sent < buffer.Length)
            {
                int count = await _socket.SendAsync(buffer[sent..], SocketFlags.None, cancellationToken).ConfigureAwait(false);

                if (count <= 0)
                {
                    throw new IOException($"Socket send made invalid progress of {count} bytes.");
                }

                if ((uint)count > (uint)(buffer.Length - sent))
                {
                    throw new IOException($"Socket send reported {count} bytes with only {buffer.Length - sent} bytes remaining.");
                }

                sent += count;
            }
        }
        finally
        {
            Volatile.Write(ref _sendActive, 0);
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
