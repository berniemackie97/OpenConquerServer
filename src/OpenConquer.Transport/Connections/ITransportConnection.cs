using System.Net;

namespace OpenConquer.Transport.Connections;

public interface ITransportConnection : IAsyncDisposable
{
    EndPoint LocalEndPoint { get; }
    EndPoint RemoteEndPoint { get; }

    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
