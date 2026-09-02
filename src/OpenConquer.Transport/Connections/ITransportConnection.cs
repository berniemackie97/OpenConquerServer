using System.Net;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Represents one established ordered byte stream connection owned by the transport layer.
/// </summary>
public interface ITransportConnection : IAsyncDisposable
{
    EndPoint LocalEndPoint { get; }
    EndPoint RemoteEndPoint { get; }

    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
