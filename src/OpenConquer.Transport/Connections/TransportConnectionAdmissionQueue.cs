using System.Threading.Channels;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Provides bounded admission and handoff for established transport
/// connections.
/// </summary>
public sealed class TransportConnectionAdmissionQueue : IAsyncDisposable
{
    private readonly Channel<ITransportConnection> _channel;

    public TransportConnectionAdmissionQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;

        _channel = Channel.CreateBounded<ITransportConnection>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        });
    }

    public int Capacity { get; }

    /// <summary>
    /// Attempts to admit a connection without waiting for capacity.
    /// Ownership transfers to the queue only when this method returns
    /// <see langword="true"/>.
    /// </summary>
    public bool TryAdmit(ITransportConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return _channel.Writer.TryWrite(connection);
    }

    /// <summary>
    /// Reads admitted connections until admission is completed.
    /// Ownership of each yielded connection transfers to the consumer.
    /// </summary>
    public IAsyncEnumerable<ITransportConnection> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Prevents further admissions while allowing already admitted
    /// connections to be drained by consumers.
    /// </summary>
    public void Complete(Exception? error = null)
    {
        _channel.Writer.TryComplete(error);
    }

    /// <summary>
    /// Completes admission and disposes connections that remain buffered.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Complete();

        List<Exception>? disposalExceptions = null;

        while (_channel.Reader.TryRead(out ITransportConnection? connection))
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (disposalExceptions ??= []).Add(exception);
            }
        }

        if (disposalExceptions is not null)
        {
            throw new AggregateException("One or more queued transport connections failed to dispose.", disposalExceptions);
        }
    }
}
