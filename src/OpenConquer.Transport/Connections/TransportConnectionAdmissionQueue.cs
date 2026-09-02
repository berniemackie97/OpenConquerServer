using System.Threading.Channels;

namespace OpenConquer.Transport.Connections;

/// <summary>
/// Provides bounded admission and handoff for established transport connections.
/// </summary>
public sealed class TransportConnectionAdmissionQueue : IAsyncDisposable
{
    private readonly Channel<ITransportConnection> _channel;
    private readonly Lock _completionGate = new();

    private readonly TaskCompletionSource _admissionCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private bool _isCompleted;

    public TransportConnectionAdmissionQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;

        _channel = Channel.CreateBounded<ITransportConnection>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = false,
                AllowSynchronousContinuations = false,
            }
        );
    }

    public int Capacity { get; }

    internal Task AdmissionCompleted => _admissionCompleted.Task;

    /// <summary>
    /// Attempts to admit a connection without waiting for capacity. Ownership transfers to the queue only when this method returns <see langword="true"/>.
    /// </summary>
    public TransportConnectionAdmissionResult TryAdmit(ITransportConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_completionGate)
        {
            if (_isCompleted)
            {
                return TransportConnectionAdmissionResult.Completed;
            }

            return _channel.Writer.TryWrite(connection)
                ? TransportConnectionAdmissionResult.Admitted
                : TransportConnectionAdmissionResult.CapacityExhausted;
        }
    }

    /// <summary>
    /// Reads admitted connections until admission is completed. Ownership of each yielded connection transfers to the consumer.
    /// </summary>
    public IAsyncEnumerable<ITransportConnection> ReadAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Prevents further admissions while allowing already admitted connections to be drained by consumers.
    /// </summary>
    public void Complete(Exception? error = null)
    {
        lock (_completionGate)
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;

            _channel.Writer.TryComplete(error);
            _admissionCompleted.TrySetResult();
        }
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
            throw new AggregateException(
                "One or more queued transport connections failed to dispose.",
                disposalExceptions
            );
        }
    }
}
