using System.Net;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Represents one established ordered byte-stream connection owned by the
/// transport layer.
/// </summary>
/// <remarks>
/// One receive operation and one send operation may execute concurrently.
/// Multiple overlapping receives or multiple overlapping sends are invalid.
///
/// Disposal is terminal and may interrupt operations already in progress.
/// Operations invoked after the connection has been disposed are invalid.
/// </remarks>
public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the local endpoint captured for this connection.
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// Gets the remote endpoint captured for this connection.
    /// </summary>
    EndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Receives up to <paramref name="buffer"/>.Length bytes from the ordered
    /// byte stream.
    /// </summary>
    /// <returns>
    /// For a non-empty buffer, a value from zero through
    /// <paramref name="buffer"/>.Length. Zero indicates that the peer has
    /// gracefully closed its sending side.
    /// </returns>
    /// <remarks>
    /// An empty buffer completes with zero without consuming stream data.
    /// </remarks>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the complete contents of <paramref name="buffer"/> in stream
    /// order.
    /// </summary>
    /// <remarks>
    /// The operation completes successfully only after every byte has been
    /// accepted by the underlying transport.
    ///
    /// If the operation fails or is canceled, some bytes may already have
    /// been accepted by the transport. Callers must not assume that failure
    /// means no wire progress occurred.
    ///
    /// An empty buffer completes without producing stream data.
    /// </remarks>
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
