using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class TransportConnectionInputTests
{
    [Fact]
    public async Task PumpAsync_WritesOrderedBytesAcrossPartialReceives()
    {
        ScriptedTransportConnection connection = new([
            [0x11, 0x22],
            [0x33],
            [0x44, 0x55],
            [],
        ]);

        Pipe pipe = new();

        Task pump = TransportConnectionInput.PumpAsync(
            connection,
            pipe.Writer,
            TestContext.Current.CancellationToken
        );

        byte[] received = await ReadAllAsync(pipe.Reader, TestContext.Current.CancellationToken);

        await pump;
        await pipe.Reader.CompleteAsync();

        Assert.Equal([0x11, 0x22, 0x33, 0x44, 0x55], received);
        Assert.Equal(4, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task PumpAsync_CompletesReaderOnImmediateEndOfStream()
    {
        ScriptedTransportConnection connection = new([
            [],
        ]);
        Pipe pipe = new();

        await TransportConnectionInput.PumpAsync(
            connection,
            pipe.Writer,
            TestContext.Current.CancellationToken
        );

        ReadResult result = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Assert.True(result.Buffer.IsEmpty);

        pipe.Reader.AdvanceTo(result.Buffer.End);

        await pipe.Reader.CompleteAsync();

        Assert.Equal(1, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task PumpAsync_PropagatesTransportFailureToPumpAndReader()
    {
        IOException failure = new("receive failed");
        FailingTransportConnection connection = new(failure);
        Pipe pipe = new();

        Task pump = TransportConnectionInput.PumpAsync(
            connection,
            pipe.Writer,
            TestContext.Current.CancellationToken
        );

        IOException pumpException = await Assert.ThrowsAsync<IOException>(() => pump);

        IOException readerException = await Assert.ThrowsAsync<IOException>(async () =>
            await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Same(failure, pumpException);
        Assert.Same(failure, readerException);

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsNegativeReceiveCount()
    {
        NegativeCountTransportConnection connection = new();
        Pipe pipe = new();

        InvalidOperationException pumpException =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransportConnectionInput.PumpAsync(
                    connection,
                    pipe.Writer,
                    TestContext.Current.CancellationToken
                )
            );

        Assert.Contains("invalid receive count of -1", pumpException.Message);

        InvalidOperationException readerException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            );

        Assert.Same(pumpException, readerException);

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsReceiveCountLargerThanDestination()
    {
        OversizedCountTransportConnection connection = new();
        Pipe pipe = new();

        InvalidOperationException pumpException =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TransportConnectionInput.PumpAsync(
                    connection,
                    pipe.Writer,
                    TestContext.Current.CancellationToken
                )
            );

        Assert.Contains("invalid receive count of", pumpException.Message);

        InvalidOperationException readerException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            );

        Assert.Same(pumpException, readerException);

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RespectsPipeBackpressure()
    {
        ScriptedTransportConnection connection = new([
            [0x01, 0x02, 0x03, 0x04, 0x05],
            [0x06],
            [],
        ]);

        Pipe pipe = new(
            new PipeOptions(
                pauseWriterThreshold: 4,
                resumeWriterThreshold: 2,
                minimumSegmentSize: 8,
                useSynchronizationContext: false
            )
        );

        Task pump = TransportConnectionInput.PumpAsync(
            connection,
            pipe.Writer,
            TestContext.Current.CancellationToken
        );

        ReadResult first = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(first.IsCompleted);
        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05], first.Buffer.ToArray());
        Assert.Equal(1, connection.ReceiveCallCount);

        pipe.Reader.AdvanceTo(first.Buffer.End);

        byte[] remainder = await ReadAllAsync(pipe.Reader, TestContext.Current.CancellationToken);

        await pump;
        await pipe.Reader.CompleteAsync();

        Assert.Equal([0x06], remainder);
        Assert.Equal(3, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task PumpAsync_PropagatesRequestedCancellationWithoutFaultingReader()
    {
        CancellationTransportConnection connection = new();
        Pipe pipe = new();

        using CancellationTokenSource cancellation = new();

        Task pump = TransportConnectionInput.PumpAsync(connection, pipe.Writer, cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);

        ReadResult result = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Assert.True(result.Buffer.IsEmpty);

        pipe.Reader.AdvanceTo(result.Buffer.End);

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsNullConnection()
    {
        Pipe pipe = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionInput.PumpAsync(
                null!,
                pipe.Writer,
                TestContext.Current.CancellationToken
            )
        );

        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task PumpAsync_RejectsNullWriter()
    {
        ScriptedTransportConnection connection = new([]);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionInput.PumpAsync(
                connection,
                null!,
                TestContext.Current.CancellationToken
            )
        );
    }

    private static async Task<byte[]> ReadAllAsync(
        PipeReader reader,
        CancellationToken cancellationToken
    )
    {
        ArrayBufferWriter<byte> output = new();

        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            ReadOnlySequence<byte> buffer = result.Buffer;

            foreach (ReadOnlyMemory<byte> segment in buffer)
            {
                Span<byte> destination = output.GetSpan(segment.Length);

                segment.Span.CopyTo(destination);
                output.Advance(segment.Length);
            }

            reader.AdvanceTo(buffer.End);

            if (result.IsCompleted)
            {
                return output.WrittenSpan.ToArray();
            }
        }
    }

    private sealed class ScriptedTransportConnection(IReadOnlyList<byte[]> receives)
        : ITransportConnection
    {
        private readonly IReadOnlyList<byte[]> _receives = receives;
        private int _receiveIndex;

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public int ReceiveCallCount { get; private set; }

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReceiveCallCount++;

            if (_receiveIndex >= _receives.Count)
            {
                return ValueTask.FromResult(0);
            }

            byte[] receive = _receives[_receiveIndex++];

            if (receive.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    "Scripted receive does not fit the supplied buffer."
                );
            }

            receive.CopyTo(buffer);

            return ValueTask.FromResult(receive.Length);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
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
            return ValueTask.FromException<int>(failure);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NegativeCountTransportConnection : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(-1);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OversizedCountTransportConnection : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(buffer.Length + 1);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationTransportConnection : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return 0;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
