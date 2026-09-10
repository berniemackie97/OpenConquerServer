using System.Buffers;
using System.Net;
using System.Threading.Channels;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Tests.Connections;

internal sealed class FakeGameTransportConnection : ITransportConnection
{
    private readonly Channel<ReceiveOperation> _receiveOperations = Channel.CreateUnbounded<ReceiveOperation>();
    private readonly ArrayBufferWriter<byte> _sent = new();
    private readonly Lock _sentGate = new();
    private readonly bool _blockDispose;
    private readonly Exception? _disposeFailure;
    private readonly TaskCompletionSource _sendCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sendBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _sendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _receiveCancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _blockSends;
    private int _disposeCount;
    private int _receiveCallCount;

    public FakeGameTransportConnection(EndPoint? localEndPoint = null, EndPoint? remoteEndPoint = null, bool blockDispose = false, Exception? disposeFailure = null)
    {
        LocalEndPoint = localEndPoint ?? new IPEndPoint(IPAddress.Loopback, 5816);
        RemoteEndPoint = remoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 40000);
        _blockDispose = blockDispose;
        _disposeFailure = disposeFailure;
    }

    public EndPoint LocalEndPoint { get; }
    public EndPoint RemoteEndPoint { get; }
    public int DisposeCount => Volatile.Read(ref _disposeCount);
    public int ReceiveCallCount => Volatile.Read(ref _receiveCallCount);
    public Task SendCompleted => _sendCompleted.Task;
    public Task SendBlocked => _sendBlocked.Task;
    public Task ReceiveStarted => _receiveStarted.Task;
    public Task ReceiveCancellationObserved => _receiveCancellationObserved.Task;
    public Task DisposeStarted => _disposeStarted.Task;

    public byte[] SentBytes
    {
        get
        {
            lock (_sentGate)
            {
                return _sent.WrittenSpan.ToArray();
            }
        }
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _receiveCallCount);
        _receiveStarted.TrySetResult();

        ReceiveOperation operation;

        try
        {
            operation = await _receiveOperations.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _receiveCancellationObserved.TrySetResult();
            throw;
        }

        if (operation.IsEndOfStream)
        {
            return 0;
        }

        if (operation.Failure is not null)
        {
            throw operation.Failure;
        }

        byte[] bytes = operation.Bytes ?? throw new InvalidOperationException("Receive operation does not contain bytes.");

        if (bytes.Length > buffer.Length)
        {
            throw new InvalidOperationException($"Queued test receive contains {bytes.Length} bytes but the transport supplied only {buffer.Length} bytes.");
        }

        bytes.CopyTo(buffer);
        return bytes.Length;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _blockSends) != 0)
        {
            _sendBlocked.TrySetResult();
            await _sendRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        lock (_sentGate)
        {
            Span<byte> destination = _sent.GetSpan(buffer.Length);
            buffer.Span.CopyTo(destination);
            _sent.Advance(buffer.Length);
        }

        _sendCompleted.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        _disposeStarted.TrySetResult();

        if (_blockDispose)
        {
            await _disposeRelease.Task.ConfigureAwait(false);
        }

        if (_disposeFailure is not null)
        {
            throw _disposeFailure;
        }
    }

    public void QueueReceive(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A queued receive must contain at least one byte.", nameof(bytes));
        }

        Queue(new ReceiveOperation(bytes.ToArray(), Failure: null, IsEndOfStream: false));
    }

    public void QueueReceiveFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Queue(new ReceiveOperation(Bytes: null, failure, IsEndOfStream: false));
    }

    public void QueueEndOfStream() => Queue(new ReceiveOperation(Bytes: null, Failure: null, IsEndOfStream: true));

    public void BlockSends()
    {
        if (Interlocked.Exchange(ref _blockSends, 1) != 0)
        {
            throw new InvalidOperationException("Test transport sends are already blocked.");
        }
    }

    public void ReleaseSends()
    {
        Volatile.Write(ref _blockSends, 0);
        _sendRelease.TrySetResult();
    }

    public void ReleaseDispose() => _disposeRelease.TrySetResult();

    private void Queue(ReceiveOperation operation)
    {
        if (!_receiveOperations.Writer.TryWrite(operation))
        {
            throw new InvalidOperationException("Unable to queue test receive operation.");
        }
    }

    private readonly record struct ReceiveOperation(byte[]? Bytes, Exception? Failure, bool IsEndOfStream);
}
