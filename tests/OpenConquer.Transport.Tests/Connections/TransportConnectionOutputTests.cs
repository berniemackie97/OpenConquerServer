using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class TransportConnectionOutputTests
{
    [Fact]
    public async Task PumpAsync_SendsBytesInOrderAcrossMultipleWrites()
    {
        RecordingTransportConnection connection = new();
        Pipe pipe = new();

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.WriteAsync(
            new byte[] { 0x11, 0x22 },
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.WriteAsync(new byte[] { 0x33 }, TestContext.Current.CancellationToken);

        await pipe.Writer.WriteAsync(
            new byte[] { 0x44, 0x55 },
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.CompleteAsync();
        await pump;

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }, connection.SentBytes);
    }

    [Fact]
    public async Task PumpAsync_CompletesWithoutSendingWhenWriterCompletesEmpty()
    {
        RecordingTransportConnection connection = new();
        Pipe pipe = new();

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.CompleteAsync();
        await pump;

        Assert.Empty(connection.SentBytes);
        Assert.Equal(0, connection.SendCallCount);
    }

    [Fact]
    public async Task PumpAsync_RespectsPipeBackpressureWhileSendIsBlocked()
    {
        BlockingTransportConnection connection = new();

        Pipe pipe = new(
            new PipeOptions(
                pauseWriterThreshold: 4,
                resumeWriterThreshold: 2,
                minimumSegmentSize: 8,
                useSynchronizationContext: false
            )
        );

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        Task<FlushResult> write = pipe
            .Writer.WriteAsync(
                new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 },
                TestContext.Current.CancellationToken
            )
            .AsTask();

        await connection.SendStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(write.IsCompleted);

        connection.Release();

        FlushResult flush = await write;

        Assert.False(flush.IsCanceled);
        Assert.False(flush.IsCompleted);

        await pipe.Writer.CompleteAsync();
        await pump;

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, connection.SentBytes);
    }

    [Fact]
    public async Task PumpAsync_PropagatesTransportFailureToPumpAndWriter()
    {
        IOException failure = new("send failed");

        FailingTransportConnection connection = new(failure);
        Pipe pipe = new();

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.WriteAsync(new byte[] { 0x11 }, TestContext.Current.CancellationToken);

        IOException pumpException = await Assert.ThrowsAsync<IOException>(() => pump);

        IOException writerException = await Assert.ThrowsAsync<IOException>(async () =>
            await pipe
                .Writer.WriteAsync(new byte[] { 0x22 }, TestContext.Current.CancellationToken)
                .AsTask()
        );

        Assert.Same(failure, pumpException);
        Assert.Same(failure, writerException);

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_PropagatesWriterFailure()
    {
        InvalidOperationException failure = new("producer failed");

        RecordingTransportConnection connection = new();
        Pipe pipe = new();

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        await pipe.Writer.CompleteAsync(failure);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                pump
        );

        Assert.Same(failure, exception);
        Assert.Empty(connection.SentBytes);
    }

    [Fact]
    public async Task PumpAsync_PropagatesRequestedCancellationWithoutFaultingWriter()
    {
        BlockingTransportConnection connection = new();
        Pipe pipe = new();

        using CancellationTokenSource cancellation = new();

        Task pump = TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            cancellation.Token
        );

        await pipe.Writer.WriteAsync(new byte[] { 0x11 }, TestContext.Current.CancellationToken);

        await connection.SendStarted.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);

        FlushResult result = await pipe.Writer.WriteAsync(
            new byte[] { 0x22 },
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsCompleted);

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_StopsWhenPendingReadIsCanceled()
    {
        RecordingTransportConnection connection = new();
        Pipe pipe = new();

        pipe.Reader.CancelPendingRead();

        await TransportConnectionOutput.PumpAsync(
            connection,
            pipe.Reader,
            TestContext.Current.CancellationToken
        );

        FlushResult result = await pipe.Writer.WriteAsync(
            new byte[] { 0x11 },
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsCompleted);
        Assert.Empty(connection.SentBytes);

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsNullConnection()
    {
        Pipe pipe = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionOutput.PumpAsync(
                null!,
                pipe.Reader,
                TestContext.Current.CancellationToken
            )
        );

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsNullReader()
    {
        RecordingTransportConnection connection = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionOutput.PumpAsync(
                connection,
                null!,
                TestContext.Current.CancellationToken
            )
        );
    }

    private sealed class RecordingTransportConnection : ITransportConnection
    {
        private readonly ArrayBufferWriter<byte> _sent = new();

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public int SendCallCount { get; private set; }

        public byte[] SentBytes => _sent.WrittenSpan.ToArray();

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            SendCallCount++;

            Span<byte> destination = _sent.GetSpan(buffer.Length);

            buffer.Span.CopyTo(destination);
            _sent.Advance(buffer.Length);

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTransportConnection : ITransportConnection
    {
        private readonly ArrayBufferWriter<byte> _sent = new();

        private readonly TaskCompletionSource _sendStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public Task SendStarted => _sendStarted.Task;

        public byte[] SentBytes => _sent.WrittenSpan.ToArray();

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            _sendStarted.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            Span<byte> destination = _sent.GetSpan(buffer.Length);

            buffer.Span.CopyTo(destination);
            _sent.Advance(buffer.Length);
        }

        public void Release()
        {
            _release.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingTransportConnection(Exception failure) : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromException(failure);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
