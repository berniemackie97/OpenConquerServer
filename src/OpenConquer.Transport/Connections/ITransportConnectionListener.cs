namespace OpenConquer.Transport.Connections;

/// <summary>
/// Accepts established transport connections
/// </summary>
public interface ITransportConnectionListener : IAsyncDisposable
{
    ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default);
}
