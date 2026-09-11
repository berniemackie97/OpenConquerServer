using System.Diagnostics.CodeAnalysis;
using System.Net;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginAdmissionTransportListenerTests
{
    [Fact]
    public void Constructor_NullDependenciesThrow()
    {
        TestTransportConnectionListener listener = new();
        TestConnectionLimiter limiter = new(true);
        Action reportSourceRejection = static () => { };
        Action<TransportConnectionRejectionDisposalFailure> reportDisposalFailure = static _ => { };

        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportListener(null!, limiter, reportSourceRejection, reportDisposalFailure));
        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportListener(listener, null!, reportSourceRejection, reportDisposalFailure));
        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportListener(listener, limiter, null!, reportDisposalFailure));
        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportListener(listener, limiter, reportSourceRejection, null!));
    }

    [Fact]
    public async Task AcceptAsync_AdmittedConnectionTransfersConnectionAndLeaseOwnership()
    {
        TestTransportConnection connection = new();
        TestTransportConnectionListener innerListener = new(connection);
        TestConnectionLimiter limiter = new(true);
        int sourceRejections = 0;

        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => sourceRejections++, static _ => throw new InvalidOperationException("Unexpected disposal failure."));
        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LoginAdmissionTransportConnection>(admitted);
        Assert.Equal(1, innerListener.AcceptCount);
        Assert.Equal([IPAddress.Parse("192.0.2.10")], limiter.RemoteAddresses);
        Assert.Equal(0, sourceRejections);
        Assert.Equal(0, connection.DisposeCount);
        Assert.Single(limiter.Leases);
        Assert.Equal(0, limiter.Leases[0].DisposeCount);

        await admitted.DisposeAsync();

        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(1, limiter.Leases[0].DisposeCount);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_RejectedSourceIsDisposedBeforeNextConnectionIsAccepted()
    {
        TestTransportConnection rejected = new();
        TestTransportConnection admittedConnection = new(remoteEndPoint: new IPEndPoint(IPAddress.Parse("192.0.2.11"), 50001));
        TestTransportConnectionListener innerListener = new(rejected, admittedConnection);
        TestConnectionLimiter limiter = new(false, true);
        int sourceRejections = 0;

        LoginAdmissionTransportListener listener = new(innerListener, limiter, () =>
        {
            Assert.Equal(1, rejected.DisposeCount);
            sourceRejections++;
        }, static _ => throw new InvalidOperationException("Unexpected disposal failure."));

        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, innerListener.AcceptCount);
        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(0, admittedConnection.DisposeCount);
        Assert.Equal(1, sourceRejections);
        Assert.Equal([IPAddress.Parse("192.0.2.10"), IPAddress.Parse("192.0.2.11")], limiter.RemoteAddresses);
        Assert.Single(limiter.Leases);

        await admitted.DisposeAsync();

        Assert.Equal(1, admittedConnection.DisposeCount);
        Assert.Equal(1, limiter.Leases[0].DisposeCount);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_RejectionDisposalFailureIsReportedAndAcceptContinues()
    {
        IOException disposalFailure = new("rejected connection disposal failed");
        TestTransportConnection rejected = new(disposeFailure: disposalFailure);
        TestTransportConnection admittedConnection = new(remoteEndPoint: new IPEndPoint(IPAddress.Parse("192.0.2.11"), 50001));
        TestTransportConnectionListener innerListener = new(rejected, admittedConnection);
        TestConnectionLimiter limiter = new(false, true);
        TransportConnectionRejectionDisposalFailure? reportedFailure = null;
        int sourceRejections = 0;

        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => sourceRejections++, failure => reportedFailure = failure);
        ITransportConnection admitted = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        Assert.True(reportedFailure.HasValue);
        Assert.Same(rejected.LocalEndPoint, reportedFailure.Value.LocalEndPoint);
        Assert.Same(rejected.RemoteEndPoint, reportedFailure.Value.RemoteEndPoint);
        Assert.Same(disposalFailure, reportedFailure.Value.Exception);
        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(1, sourceRejections);
        Assert.Equal(2, innerListener.AcceptCount);

        await admitted.DisposeAsync();
        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_SourceRejectionReportingFailureTerminatesAccept()
    {
        InvalidOperationException reportingFailure = new("source rejection reporting failed");
        TestTransportConnection rejected = new();
        TestTransportConnectionListener innerListener = new(rejected);
        TestConnectionLimiter limiter = new(false);
        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => throw reportingFailure, static _ => { });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(reportingFailure, exception.InnerException);
        Assert.Equal(1, rejected.DisposeCount);
        Assert.Equal(1, innerListener.AcceptCount);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_RejectionDisposalFailureReportingFailurePreservesBothFailures()
    {
        IOException disposalFailure = new("rejected connection disposal failed");
        InvalidOperationException reportingFailure = new("disposal failure reporting failed");
        TestTransportConnection rejected = new(disposeFailure: disposalFailure);
        TestTransportConnectionListener innerListener = new(rejected);
        TestConnectionLimiter limiter = new(false);
        int sourceRejections = 0;
        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => sourceRejections++, _ => throw reportingFailure);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(disposalFailure, exception.InnerExceptions[0]);
        Assert.Same(reportingFailure, exception.InnerExceptions[1]);
        Assert.Equal(0, sourceRejections);
        Assert.Equal(1, rejected.DisposeCount);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_LimiterFailureDisposesAcceptedConnection()
    {
        InvalidOperationException admissionFailure = new("connection limiter failed");
        TestTransportConnection connection = new();
        TestTransportConnectionListener innerListener = new(connection);
        ThrowingConnectionLimiter limiter = new(admissionFailure);
        int sourceRejections = 0;
        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => sourceRejections++, static _ => { });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(admissionFailure, exception);
        Assert.Equal(1, innerListener.AcceptCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(1, limiter.CallCount);
        Assert.Equal(0, sourceRejections);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_LimiterFailureAndConnectionDisposalFailurePreservesBothFailures()
    {
        InvalidOperationException admissionFailure = new("connection limiter failed");
        IOException disposalFailure = new("connection disposal failed");
        TestTransportConnection connection = new(disposeFailure: disposalFailure);
        TestTransportConnectionListener innerListener = new(connection);
        ThrowingConnectionLimiter limiter = new(admissionFailure);
        int sourceRejections = 0;
        LoginAdmissionTransportListener listener = new(innerListener, limiter, () => sourceRejections++, static _ => { });

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(admissionFailure, exception.InnerExceptions[0]);
        Assert.Same(disposalFailure, exception.InnerExceptions[1]);
        Assert.Equal(1, innerListener.AcceptCount);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Equal(1, limiter.CallCount);
        Assert.Equal(0, sourceRejections);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_NonIpRemoteEndpointDisposesConnectionAndFails()
    {
        TestTransportConnection connection = new(remoteEndPoint: new DnsEndPoint("example.test", 50000));
        TestTransportConnectionListener innerListener = new(connection);
        TestConnectionLimiter limiter = new(true);
        LoginAdmissionTransportListener listener = new(innerListener, limiter, static () => { }, static _ => { });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The AccountServer login listener accepted a connection without an IP remote endpoint.", exception.Message);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Empty(limiter.RemoteAddresses);
        Assert.Empty(limiter.Leases);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_NonIpRemoteEndpointAndDisposalFailurePreservesBothFailures()
    {
        IOException disposalFailure = new("connection disposal failed");
        TestTransportConnection connection = new(remoteEndPoint: new DnsEndPoint("example.test", 50000), disposeFailure: disposalFailure);
        TestTransportConnectionListener innerListener = new(connection);
        TestConnectionLimiter limiter = new(true);
        LoginAdmissionTransportListener listener = new(innerListener, limiter, static () => { }, static _ => { });

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(exception.InnerExceptions[0]);
        Assert.Same(disposalFailure, exception.InnerExceptions[1]);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Empty(limiter.RemoteAddresses);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task AcceptAsync_PreCanceledOperationDoesNotAcceptConnection()
    {
        TestTransportConnection connection = new();
        TestTransportConnectionListener innerListener = new(connection);
        TestConnectionLimiter limiter = new(true);
        LoginAdmissionTransportListener listener = new(innerListener, limiter, static () => { }, static _ => { });

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.AcceptAsync(cancellation.Token).AsTask());

        Assert.Equal(0, innerListener.AcceptCount);
        Assert.Equal(0, connection.DisposeCount);
        Assert.Empty(limiter.RemoteAddresses);

        await listener.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ForwardsToInnerListener()
    {
        TestTransportConnectionListener innerListener = new();
        TestConnectionLimiter limiter = new(true);
        LoginAdmissionTransportListener listener = new(innerListener, limiter, static () => { }, static _ => { });

        await listener.DisposeAsync();

        Assert.Equal(1, innerListener.DisposeCount);
    }

    private sealed class TestConnectionLimiter : IAccountLoginConnectionLimiter
    {
        private readonly Queue<bool> _decisions;

        public TestConnectionLimiter(params bool[] decisions)
        {
            _decisions = new Queue<bool>(decisions);
        }

        public List<IPAddress> RemoteAddresses { get; } = [];
        public List<TestAdmissionLease> Leases { get; } = [];

        public bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection)
        {
            RemoteAddresses.Add(remoteAddress);

            bool admitted = _decisions.Count > 0 ? _decisions.Dequeue() : throw new InvalidOperationException("No admission decision was configured.");

            if (!admitted)
            {
                connection = null;
                return false;
            }

            TestAdmissionLease lease = new();
            Leases.Add(lease);
            connection = lease;
            return true;
        }
    }

    private sealed class ThrowingConnectionLimiter(Exception failure) : IAccountLoginConnectionLimiter
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection)
        {
            ArgumentNullException.ThrowIfNull(remoteAddress);
            Interlocked.Increment(ref _callCount);
            connection = null;
            throw failure;
        }
    }

    private sealed class TestAdmissionLease : IAccountLoginConnectionLease
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class TestTransportConnectionListener : ITransportConnectionListener
    {
        private readonly Queue<ITransportConnection> _connections;
        private int _acceptCount;
        private int _disposeCount;

        public TestTransportConnectionListener(params ITransportConnection[] connections)
        {
            _connections = new Queue<ITransportConnection>(connections);
        }

        public int AcceptCount => Volatile.Read(ref _acceptCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acceptCount);

            if (_connections.Count == 0)
            {
                throw new InvalidOperationException("No test connection was configured.");
            }

            return ValueTask.FromResult(_connections.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Exception? _disposeFailure;
        private int _disposeCount;

        public TestTransportConnection(EndPoint? localEndPoint = null, EndPoint? remoteEndPoint = null, Exception? disposeFailure = null)
        {
            LocalEndPoint = localEndPoint ?? new IPEndPoint(IPAddress.Loopback, 9958);
            RemoteEndPoint = remoteEndPoint ?? new IPEndPoint(IPAddress.Parse("192.0.2.10"), 50000);
            _disposeFailure = disposeFailure;
        }

        public EndPoint LocalEndPoint { get; }
        public EndPoint RemoteEndPoint { get; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return _disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(_disposeFailure);
        }
    }
}
