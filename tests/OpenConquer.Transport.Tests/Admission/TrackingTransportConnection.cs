using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Admission;

internal sealed class TrackingTransportConnection(Exception? disposalException = null) : ITransportConnection
{
    public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

    public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

    public int DisposeCallCount { get; private set; }

    public bool IsDisposed => DisposeCallCount != 0;

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;

        return disposalException is null ? ValueTask.CompletedTask : ValueTask.FromException(disposalException);
    }
}
