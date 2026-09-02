using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class TransportConnectionAdmissionQueueTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidCapacity(int capacity)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TransportConnectionAdmissionQueue(capacity)
        );

        Assert.Equal("capacity", exception.ParamName);
    }

    [Fact]
    public async Task TryAdmit_RejectsNullConnection()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        Assert.Throws<ArgumentNullException>(() => queue.TryAdmit(null!));

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task TryAdmit_EnforcesCapacityWithoutTakingOwnershipOfRejectedConnection()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 2);

        TrackingTransportConnection first = new();
        TrackingTransportConnection second = new();
        TrackingTransportConnection rejected = new();

        Assert.Equal(2, queue.Capacity);

        Assert.True(queue.TryAdmit(first));
        Assert.True(queue.TryAdmit(second));
        Assert.False(queue.TryAdmit(rejected));

        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.False(rejected.IsDisposed);

        await queue.DisposeAsync();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.False(rejected.IsDisposed);

        await rejected.DisposeAsync();
    }

    [Fact]
    public async Task Complete_PreventsFurtherAdmissionWithoutTakingOwnership()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 1);
        TrackingTransportConnection connection = new();

        queue.Complete();
        queue.Complete();

        Assert.False(queue.TryAdmit(connection));
        Assert.False(connection.IsDisposed);

        await queue.DisposeAsync();

        Assert.False(connection.IsDisposed);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ReadAllAsync_PreservesAdmissionOrderAndTransfersOwnership()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 3);

        TrackingTransportConnection first = new();
        TrackingTransportConnection second = new();
        TrackingTransportConnection third = new();

        Assert.True(queue.TryAdmit(first));
        Assert.True(queue.TryAdmit(second));
        Assert.True(queue.TryAdmit(third));

        queue.Complete();

        List<ITransportConnection> connections = [];

        await foreach (
            ITransportConnection connection in queue.ReadAllAsync(
                TestContext.Current.CancellationToken
            )
        )
        {
            connections.Add(connection);
        }

        Assert.Equal([first, second, third], connections);

        await queue.DisposeAsync();

        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.False(third.IsDisposed);

        await first.DisposeAsync();
        await second.DisposeAsync();
        await third.DisposeAsync();
    }

    [Fact]
    public async Task ReadAllAsync_PropagatesCancellation()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await using IAsyncEnumerator<ITransportConnection> enumerator = queue
            .ReadAllAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Task<bool> pendingRead = enumerator.MoveNextAsync().AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task Complete_WithErrorDrainsBufferedConnectionsBeforePropagatingFailure()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        TrackingTransportConnection connection = new();
        InvalidOperationException failure = new("admission failed");

        Assert.True(queue.TryAdmit(connection));

        queue.Complete(failure);

        await using IAsyncEnumerator<ITransportConnection> enumerator = queue
            .ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Same(connection, enumerator.Current);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                enumerator.MoveNextAsync().AsTask()
        );

        Assert.Same(failure, exception);

        await queue.DisposeAsync();

        Assert.False(connection.IsDisposed);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ContinuesCleanupAfterIndividualDisposalFailures()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 3);

        InvalidOperationException firstFailure = new("first disposal failed");
        IOException secondFailure = new("second disposal failed");

        TrackingTransportConnection first = new(firstFailure);
        TrackingTransportConnection second = new();
        TrackingTransportConnection third = new(secondFailure);

        Assert.True(queue.TryAdmit(first));
        Assert.True(queue.TryAdmit(second));
        Assert.True(queue.TryAdmit(third));

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            queue.DisposeAsync().AsTask()
        );

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(firstFailure, exception.InnerExceptions[0]);
        Assert.Same(secondFailure, exception.InnerExceptions[1]);

        Assert.Equal(1, first.DisposeCallCount);
        Assert.Equal(1, second.DisposeCallCount);
        Assert.Equal(1, third.DisposeCallCount);
    }

    private sealed class TrackingTransportConnection(Exception? disposalException = null)
        : ITransportConnection
    {
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1000);

        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2000);

        public int DisposeCallCount { get; private set; }

        public bool IsDisposed => DisposeCallCount != 0;

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
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;

            return disposalException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(disposalException);
        }
    }
}
