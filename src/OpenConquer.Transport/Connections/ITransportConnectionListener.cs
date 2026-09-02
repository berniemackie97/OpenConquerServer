namespace OpenConquer.Transport.Connections;

/// <summary>
/// Accepts established transport connections and transfers ownership of each
/// successful accept to the caller.
/// </summary>
public interface ITransportConnectionListener : IAsyncDisposable
{
    ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default);
}
