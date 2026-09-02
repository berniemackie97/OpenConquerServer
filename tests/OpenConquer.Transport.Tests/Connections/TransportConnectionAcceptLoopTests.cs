using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Tests.Connections;

public sealed class TransportConnectionAcceptLoopTests
{
    [Fact]
    public async Task RunAsync_RejectsNullListener()
    {
        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionAcceptLoop.RunAsync(
                null!,
                queue,
                FailOnRejectionDisposalFailure,
                TestContext.Current.CancellationToken
            )
        );

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_RejectsNullAdmissionQueue()
    {
        await using SocketTransportListener listener = CreateListener();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionAcceptLoop.RunAsync(
                listener,
                null!,
                FailOnRejectionDisposalFailure,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task RunAsync_RejectsNullRejectionDisposalFailureReporter()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransportConnectionAcceptLoop.RunAsync(
                listener,
                queue,
                null!,
                TestContext.Current.CancellationToken
            )
        );

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_TransfersAcceptedConnectionToAdmissionQueue()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            FailOnRejectionDisposalFailure,
            cancellation.Token
        );

        using Socket client = CreateClientSocket();

        await client.ConnectAsync(listener.LocalEndPoint, TestContext.Current.CancellationToken);

        ITransportConnection connection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptLoop);

        byte[] sent = [0x11];

        int sentCount = await client.SendAsync(
            sent,
            SocketFlags.None,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, sentCount);

        byte[] received = new byte[1];

        int receivedCount = await connection.ReceiveAsync(
            received,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, receivedCount);
        Assert.Equal(0x11, received[0]);

        await connection.DisposeAsync();
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_RejectsOverloadAndContinuesAccepting()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        TrackingTransportConnection occupyingConnection = new();

        Assert.Equal(
            TransportConnectionAdmissionResult.Admitted,
            queue.TryAdmit(occupyingConnection)
        );

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            FailOnRejectionDisposalFailure,
            cancellation.Token
        );

        using Socket rejectedClient = CreateClientSocket();

        await rejectedClient.ConnectAsync(
            listener.LocalEndPoint,
            TestContext.Current.CancellationToken
        );

        await AssertRemoteClosedAsync(rejectedClient);

        ITransportConnection transferredOccupyingConnection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        Assert.Same(occupyingConnection, transferredOccupyingConnection);

        using Socket admittedClient = CreateClientSocket();

        await admittedClient.ConnectAsync(
            listener.LocalEndPoint,
            TestContext.Current.CancellationToken
        );

        ITransportConnection admittedConnection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptLoop);

        await transferredOccupyingConnection.DisposeAsync();
        await admittedConnection.DisposeAsync();
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_ReportsOverloadDisposalFailureAndContinuesAccepting()
    {
        await using ControlledTransportConnectionListener listener = new();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        TrackingTransportConnection occupyingConnection = new();

        Assert.Equal(
            TransportConnectionAdmissionResult.Admitted,
            queue.TryAdmit(occupyingConnection)
        );

        IOException disposalFailure = new("rejected connection disposal failed");

        TrackingTransportConnection rejectedConnection = new(disposalFailure);

        TrackingTransportConnection admittedConnection = new();

        TaskCompletionSource<TransportConnectionRejectionDisposalFailure> reportedFailure = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            failure => reportedFailure.TrySetResult(failure),
            cancellation.Token
        );

        listener.QueueConnection(rejectedConnection);

        TransportConnectionRejectionDisposalFailure reported = await reportedFailure.Task.WaitAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Same(disposalFailure, reported.Exception);

        Assert.Equal(rejectedConnection.LocalEndPoint, reported.LocalEndPoint);

        Assert.Equal(rejectedConnection.RemoteEndPoint, reported.RemoteEndPoint);

        Assert.Equal(1, rejectedConnection.DisposeCallCount);

        Assert.False(acceptLoop.IsCompleted);

        ITransportConnection transferredOccupyingConnection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        listener.QueueConnection(admittedConnection);

        ITransportConnection transferredAdmittedConnection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        Assert.Same(admittedConnection, transferredAdmittedConnection);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptLoop);

        await transferredOccupyingConnection.DisposeAsync();
        await transferredAdmittedConnection.DisposeAsync();
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_PreservesDisposalFailureWhenReporterThrows()
    {
        await using ControlledTransportConnectionListener listener = new();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        TrackingTransportConnection occupyingConnection = new();

        Assert.Equal(
            TransportConnectionAdmissionResult.Admitted,
            queue.TryAdmit(occupyingConnection)
        );

        IOException disposalFailure = new("rejected connection disposal failed");

        InvalidOperationException reportingFailure = new("failure reporting failed");

        TrackingTransportConnection rejectedConnection = new(disposalFailure);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            _ => throw reportingFailure,
            TestContext.Current.CancellationToken
        );

        listener.QueueConnection(rejectedConnection);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            acceptLoop
        );

        Assert.Equal(2, exception.InnerExceptions.Count);

        Assert.Same(disposalFailure, exception.InnerExceptions[0]);

        Assert.Same(reportingFailure, exception.InnerExceptions[1]);

        Assert.Equal(1, rejectedConnection.DisposeCallCount);

        ITransportConnection transferredOccupyingConnection = await ReadOneAsync(
            queue,
            TestContext.Current.CancellationToken
        );

        await transferredOccupyingConnection.DisposeAsync();
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_StopsImmediatelyWhenAdmissionIsCompleted()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        queue.Complete();

        await TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            FailOnRejectionDisposalFailure,
            TestContext.Current.CancellationToken
        );

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_StopsWhenAdmissionCompletesDuringPendingAccept()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            FailOnRejectionDisposalFailure,
            TestContext.Current.CancellationToken
        );

        Assert.False(acceptLoop.IsCompleted);

        queue.Complete();

        await acceptLoop.WaitAsync(TestContext.Current.CancellationToken);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_CancellationDoesNotCompleteAdmissionQueue()
    {
        await using SocketTransportListener listener = CreateListener();

        TransportConnectionAdmissionQueue queue = new(capacity: 1);

        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Task acceptLoop = TransportConnectionAcceptLoop.RunAsync(
            listener,
            queue,
            FailOnRejectionDisposalFailure,
            cancellation.Token
        );

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acceptLoop);

        TrackingTransportConnection connection = new();

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));

        await queue.DisposeAsync();

        Assert.True(connection.IsDisposed);
    }

    private static SocketTransportListener CreateListener()
    {
        return new SocketTransportListener(
            new IPEndPoint(IPAddress.Loopback, port: 0),
            backlog: 16
        );
    }

    private static Socket CreateClientSocket()
    {
        return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    private static async Task<ITransportConnection> ReadOneAsync(
        TransportConnectionAdmissionQueue queue,
        CancellationToken cancellationToken
    )
    {
        await using IAsyncEnumerator<ITransportConnection> enumerator = queue
            .ReadAllAsync(cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        bool available = await enumerator.MoveNextAsync();

        Assert.True(available);

        return enumerator.Current;
    }

    private static async Task AssertRemoteClosedAsync(Socket socket)
    {
        byte[] buffer = new byte[1];

        int received = await socket.ReceiveAsync(
            buffer,
            SocketFlags.None,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, received);
    }

    private static void FailOnRejectionDisposalFailure(
        TransportConnectionRejectionDisposalFailure failure
    )
    {
        throw new InvalidOperationException(
            "Unexpected rejected connection disposal failure.",
            failure.Exception
        );
    }

    private sealed class ControlledTransportConnectionListener : ITransportConnectionListener
    {
        private readonly Channel<ITransportConnection> _connections =
            Channel.CreateUnbounded<ITransportConnection>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                }
            );

        public async ValueTask<ITransportConnection> AcceptAsync(
            CancellationToken cancellationToken = default
        )
        {
            return await _connections.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        public void QueueConnection(ITransportConnection connection)
        {
            if (!_connections.Writer.TryWrite(connection))
            {
                throw new InvalidOperationException(
                    "The controlled transport listener is completed."
                );
            }
        }

        public ValueTask DisposeAsync()
        {
            _connections.Writer.TryComplete();

            return ValueTask.CompletedTask;
        }
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
