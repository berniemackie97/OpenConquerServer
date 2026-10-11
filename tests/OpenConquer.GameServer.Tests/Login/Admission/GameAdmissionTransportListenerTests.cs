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
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });

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
    public async Task AcceptAsync_ReleasesSourceCapacityAfterSuccessfulDisposal()
    {
        FakeGameTransportConnection first = CreateConnection(s_firstAddress, 40001);
        FakeGameTransportConnection second = CreateConnection(s_firstAddress, 40002);

        await using TestListener transport = new(first);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });

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
    public async Task AcceptAsync_FailedDisposalRetainsSourceReservationAndReportsFatalFailure()
    {
        IOException disposalFailure = new("transport disposal failed");
        FakeGameTransportConnection first = new(
            remoteEndPoint: new IPEndPoint(s_firstAddress, 40001),
            disposeFailure: disposalFailure);

        FakeGameTransportConnection rejected = CreateConnection(s_firstAddress, 40002);
        FakeGameTransportConnection otherSource = CreateConnection(s_secondAddress, 40003);
        List<Exception> fatalFailures = [];

        await using TestListener transport = new(first);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, fatalFailures.Add);

        ITransportConnection initial = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        IOException observed = await Assert.ThrowsAsync<IOException>(
            () => initial.DisposeAsync().AsTask());

        Assert.Same(disposalFailure, observed);
        Assert.Collection(fatalFailures, failure => Assert.Same(disposalFailure, failure));

        IOException repeated = await Assert.ThrowsAsync<IOException>(
            () => initial.DisposeAsync().AsTask());

        Assert.Same(disposalFailure, repeated);
        Assert.Single(fatalFailures);
        Assert.Equal(1, first.DisposeCount);

        transport.Queue(rejected);
        transport.Queue(otherSource);

        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(s_secondAddress, ((IPEndPoint)admitted.RemoteEndPoint).Address);
        Assert.Equal(0, otherSource.DisposeCount);

        await admitted.DisposeAsync();

        Assert.Equal(1, otherSource.DisposeCount);
    }

    [Fact]
    public async Task AcceptAsync_RejectedTransportDisposalFailureReportsFatalFailure()
    {
        IOException disposalFailure = new("rejected transport disposal failed");
        FakeGameTransportConnection first = CreateConnection(s_firstAddress, 40001);
        FakeGameTransportConnection rejected = new(
            remoteEndPoint: new IPEndPoint(s_firstAddress, 40002),
            disposeFailure: disposalFailure);

        List<Exception> fatalFailures = [];

        await using TestListener transport = new(first, rejected);
        using GameLoginMetrics metrics = new();
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, fatalFailures.Add);

        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        IOException observed = await Assert.ThrowsAsync<IOException>(
            () => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(disposalFailure, observed);
        Assert.Collection(fatalFailures, failure => Assert.Same(disposalFailure, failure));
        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(0, first.DisposeCount);

        await admitted.DisposeAsync();

        Assert.Equal(1, first.DisposeCount);
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
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });

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
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });

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
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });
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
        await using GameAdmissionTransportListener listener = new(transport, 1, metrics, static _ => { });

        long started = Stopwatch.GetTimestamp();

        Task<(ITransportConnection Connection, double LatencyMilliseconds)>[] admissions = connections.Select(_ => Task.Run(async () =>
        {
            long operationStarted = Stopwatch.GetTimestamp();
            ITransportConnection connection = await listener.AcceptAsync(TestContext.Current.CancellationToken);

            return (connection, Stopwatch.GetElapsedTime(operationStarted).TotalMilliseconds);
        }, TestContext.Current.CancellationToken)).ToArray();

        (ITransportConnection Connection, double LatencyMilliseconds)[] admitted = await Task.WhenAll(admissions);

        double admissionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        double[] latencies = admitted.Select(result => result.LatencyMilliseconds).Order().ToArray();

        Assert.Equal(connectionCount, admitted.Length);
        Assert.Equal(connectionCount, admitted.Select(result => ((IPEndPoint)result.Connection.RemoteEndPoint).Address).Distinct().Count());

        await Task.WhenAll(admitted.Select(result => result.Connection.DisposeAsync().AsTask()));

        Assert.All(connections, connection => Assert.Equal(1, connection.DisposeCount));
        Assert.Equal(connectionCount, transport.AcceptCount);

        Console.WriteLine(
            $"Game admission concurrency: {connectionCount} connections; " +
            $"elapsed={admissionMilliseconds:F3} ms; " +
            $"throughput={connectionCount / Math.Max(admissionMilliseconds / 1000.0, double.Epsilon):F0}/s; " +
            $"p50={Percentile(latencies, 0.50):F3} ms; " +
            $"p95={Percentile(latencies, 0.95):F3} ms; " +
            $"p99={Percentile(latencies, 0.99):F3} ms.");
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)];
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
