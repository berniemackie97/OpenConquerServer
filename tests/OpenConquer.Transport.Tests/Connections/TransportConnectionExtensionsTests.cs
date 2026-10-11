using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class TransportConnectionExtensionsTests
{
    [Fact]
    public async Task TryReceiveExactlyAsync_FillsBufferAcrossPartialReceives()
    {
        ScriptedTransportConnection connection = new([
            new byte[] { 0x11, 0x22 },
            new byte[] { 0x33 },
            new byte[] { 0x44, 0x55 },
        ]);

        byte[] buffer = new byte[5];

        bool result = await connection.TryReceiveExactlyAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal([0x11, 0x22, 0x33, 0x44, 0x55], buffer);
        Assert.Equal(3, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_ReturnsFalseOnImmediateEndOfStream()
    {
        ScriptedTransportConnection connection = new([Array.Empty<byte>()]);

        byte[] buffer = new byte[3];

        bool result = await connection.TryReceiveExactlyAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal([0x00, 0x00, 0x00], buffer);
        Assert.Equal(1, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_ReturnsFalseWhenEndOfStreamFollowsPartialProgress()
    {
        ScriptedTransportConnection connection = new([
            new byte[] { 0x11, 0x22 },
            Array.Empty<byte>(),
        ]);

        byte[] buffer = new byte[4];

        bool result = await connection.TryReceiveExactlyAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal([0x11, 0x22, 0x00, 0x00], buffer);
        Assert.Equal(2, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_EmptyBufferSucceedsWithoutReceiving()
    {
        ScriptedTransportConnection connection = new([]);

        bool result = await connection.TryReceiveExactlyAsync(Memory<byte>.Empty, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(0, connection.ReceiveCallCount);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_RejectsNegativeReceiveCount()
    {
        InvalidCountTransportConnection connection = new(-1);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await connection.TryReceiveExactlyAsync(new byte[1], cancellationToken: TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("invalid receive count of -1", exception.Message);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_RejectsReceiveCountLargerThanRemainingBuffer()
    {
        InvalidCountTransportConnection connection = new(2);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await connection.TryReceiveExactlyAsync(new byte[1], cancellationToken: TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("invalid receive count of 2", exception.Message);
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_PropagatesCancellation()
    {
        CancellationTransportConnection connection = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await connection.TryReceiveExactlyAsync(new byte[1], cancellation.Token).AsTask()
        );
    }

    [Fact]
    public async Task TryReceiveExactlyAsync_RejectsNullConnection()
    {
        ITransportConnection? connection = null;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await TransportConnectionExtensions
                .TryReceiveExactlyAsync(connection!, new byte[1], TestContext.Current.CancellationToken).AsTask()
        );
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InvalidCountTransportConnection(int count) : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.FromResult(count);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationTransportConnection : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
