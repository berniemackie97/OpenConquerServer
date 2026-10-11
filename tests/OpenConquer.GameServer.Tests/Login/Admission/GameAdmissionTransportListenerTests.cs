using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using OpenConquer.GameServer.Login.Admission;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.GameServer.Tests.Connections;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Tests.Login.Admission;

public sealed class GameAdmissionTransportListenerTests
{
    private static readonly IPAddress s_firstAddress = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress s_secondAddress = IPAddress.Parse("192.0.2.11");

    [Fact]
    public async Task AcceptAsync_SourceCapacityRejectsExcessConnectionsAndRetainsOtherSources()
    {
        FakeGameTransportConnection first = CreateConnection(s_firstAddress, 40001);
        FakeGameTransportConnection rejected = CreateConnection(s_firstAddress, 40002);
        FakeGameTransportConnection secondSource = CreateConnection(s_secondAddress, 40003);

        await using TestListener transport = new(first, rejected, secondSource);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        ITransportConnection admittedFirst = await listener.AcceptAsync(TestContext.Current.CancellationToken);
        ITransportConnection admittedSecond = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(0, secondSource.DisposeCount);
        Assert.Same(s_firstAddress, ((IPEndPoint)admittedFirst.RemoteEndPoint).Address);
        Assert.Same(s_secondAddress, ((IPEndPoint)admittedSecond.RemoteEndPoint).Address);

        await admittedFirst.DisposeAsync();
        await admittedSecond.DisposeAsync();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, secondSource.DisposeCount);
    }

    [Fact]
    public async Task AcceptAsync_ReleasesSourceCapacityAfterConnectionDisposal()
    {
        FakeGameTransportConnection first = CreateConnection(s_firstAddress, 40001);
        FakeGameTransportConnection second = CreateConnection(s_firstAddress, 40002);

        await using TestListener transport = new(first);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        ITransportConnection initial = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        await initial.DisposeAsync();
        await initial.DisposeAsync();

        transport.Queue(second);

        ITransportConnection replacement = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);

        await replacement.DisposeAsync();

        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task AcceptAsync_ReleasesSourceCapacityEvenWhenDisposalFails()
    {
        IOException failure = new("transport disposal failed");
        FakeGameTransportConnection first = new(
            remoteEndPoint: new IPEndPoint(s_firstAddress, 40001),
            disposeFailure: failure);

        FakeGameTransportConnection replacement = CreateConnection(s_firstAddress, 40002);

        await using TestListener transport = new(first);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        ITransportConnection initial = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        IOException observed = await Assert.ThrowsAsync<IOException>(
            () => initial.DisposeAsync().AsTask());

        Assert.Same(failure, observed);

        transport.Queue(replacement);

        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, replacement.DisposeCount);

        await admitted.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_DisposalInProgressStillOccupiesSourceCapacity()
    {
        FakeGameTransportConnection first = new(
            remoteEndPoint: new IPEndPoint(s_firstAddress, 40001),
            blockDispose: true);

        FakeGameTransportConnection rejected = CreateConnection(s_firstAddress, 40002);
        FakeGameTransportConnection secondSource = CreateConnection(s_secondAddress, 40003);
        FakeGameTransportConnection replacement = CreateConnection(s_firstAddress, 40004);

        await using TestListener transport = new(first, rejected, secondSource);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        ITransportConnection initial = await listener.AcceptAsync(TestContext.Current.CancellationToken);
        Task pendingDisposal = initial.DisposeAsync().AsTask();

        await first.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        ITransportConnection other = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(0, secondSource.DisposeCount);
        Assert.False(pendingDisposal.IsCompleted);

        first.ReleaseDispose();
        await pendingDisposal.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

        transport.Queue(replacement);

        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        await admitted.DisposeAsync();
        await other.DisposeAsync();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, replacement.DisposeCount);
        Assert.Equal(1, secondSource.DisposeCount);
    }

    [Fact]
    public async Task AcceptAsync_NonIpRemoteEndpointFailsClosedAndDisposesTransport()
    {
        FakeGameTransportConnection invalid = new(
            remoteEndPoint: new DnsEndPoint("invalid.example", 40001));

        await using TestListener transport = new(invalid);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("IP remote endpoint", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, invalid.DisposeCount);
    }

    [Fact]
    public async Task AcceptAsync_CanceledOperationDoesNotConsumeTransport()
    {
        FakeGameTransportConnection connection = CreateConnection(s_firstAddress, 40001);

        await using TestListener transport = new(connection);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);
        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => listener.AcceptAsync(cancellation.Token).AsTask());

        Assert.Equal(0, connection.DisposeCount);
        Assert.Equal(0, transport.AcceptCount);
    }

    [Fact]
    public async Task AcceptAsync_ConcurrentDistinctSourcesPreserveIndependentOccupancy()
    {
        const int connectionCount = 256;

        FakeGameTransportConnection[] connections = new FakeGameTransportConnection[connectionCount];

        for (int index = 0; index < connections.Length; index++)
        {
            connections[index] = CreateConnection(
                new IPAddress([198, 18, (byte)(index / 256), (byte)(index % 256)]),
                40000 + index);
        }

        await using TestListener transport = new(connections);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics);

        ITransportConnection[] admitted = new ITransportConnection[connectionCount];

        long started = Stopwatch.GetTimestamp();

        for (int index = 0; index < admitted.Length; index++)
        {
            admitted[index] = await listener.AcceptAsync(TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(admitted.Select(connection => connection.DisposeAsync().AsTask()));

        double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Assert.All(connections, connection => Assert.Equal(1, connection.DisposeCount));
        Assert.Equal(connectionCount, transport.AcceptCount);

        Console.WriteLine($"Game admission: {connectionCount} distinct sources; {elapsedMilliseconds:F3} ms; " +
            $"{connectionCount / Math.Max(elapsedMilliseconds / 1000.0, double.Epsilon):F0} connections/second.");
    }

    private static FakeGameTransportConnection CreateConnection(IPAddress source, int port)
    {
        return new FakeGameTransportConnection(remoteEndPoint: new IPEndPoint(source, port));
    }

    private sealed class TestListener : ITransportConnectionListener
    {
        private readonly Channel<ITransportConnection> _connections = Channel.CreateUnbounded<ITransportConnection>();
        private int _acceptCount;

        public TestListener(params ITransportConnection[] connections)
        {
            foreach (ITransportConnection connection in connections)
            {
                Queue(connection);
            }
        }

        public int AcceptCount => Volatile.Read(ref _acceptCount);

        public void Queue(ITransportConnection connection)
        {
            if (!_connections.Writer.TryWrite(connection))
            {
                throw new InvalidOperationException("Test listener cannot enqueue a connection.");
            }
        }

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ITransportConnection connection = await _connections.Reader.ReadAsync(cancellationToken);
            Interlocked.Increment(ref _acceptCount);
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            _connections.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
