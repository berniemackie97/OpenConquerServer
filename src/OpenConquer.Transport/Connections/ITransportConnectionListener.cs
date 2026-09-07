namespace OpenConquer.Transport.Connections;

public interface ITransportConnectionListener : IAsyncDisposable
{
    ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default);
}
