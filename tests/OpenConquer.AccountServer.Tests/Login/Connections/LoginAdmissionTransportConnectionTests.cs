using System.Net;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Connections;

public sealed class LoginAdmissionTransportConnectionTests
{
    [Fact]
    public void Constructor_NullDependenciesThrow()
    {
        TestTransportConnection connection = new();
        TestAdmissionLease lease = new();

        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportConnection(null!, lease));
        Assert.Throws<ArgumentNullException>(() => new LoginAdmissionTransportConnection(connection, null!));
    }

    [Fact]
    public async Task Connection_ForwardsEndpointsAndIo()
    {
        TestTransportConnection inner = new(receiveBytes: [1, 2, 3]);
        TestAdmissionLease lease = new();
        await using LoginAdmissionTransportConnection connection = new(inner, lease);

        byte[] receiveBuffer = new byte[8];
        int received = await connection.ReceiveAsync(receiveBuffer, TestContext.Current.CancellationToken);
        await connection.SendAsync(new byte[] { 4, 5, 6 }, TestContext.Current.CancellationToken);

        Assert.Same(inner.LocalEndPoint, connection.LocalEndPoint);
        Assert.Same(inner.RemoteEndPoint, connection.RemoteEndPoint);
        Assert.Equal(3, received);
        Assert.Equal([1, 2, 3], receiveBuffer[..received]);
        Assert.Equal([4, 5, 6], inner.SentBytes);
    }

    [Fact]
    public async Task DisposeAsync_DisposesConnectionAndReleasesLeaseExactlyOnce()
    {
        TestTransportConnection inner = new();
        TestAdmissionLease lease = new();
        LoginAdmissionTransportConnection connection = new(inner, lease);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesLeaseWhenConnectionDisposalFails()
    {
        IOException failure = new("connection disposal failed");
        TestTransportConnection inner = new(disposeFailure: failure);
        TestAdmissionLease lease = new();
        LoginAdmissionTransportConnection connection = new(inner, lease);

        IOException exception = await Assert.ThrowsAsync<IOException>(() => connection.DisposeAsync().AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_PreservesConnectionAndLeaseDisposalFailures()
    {
        IOException connectionFailure = new("connection disposal failed");
        InvalidOperationException leaseFailure = new("lease disposal failed");
        TestTransportConnection inner = new(disposeFailure: connectionFailure);
        TestAdmissionLease lease = new(leaseFailure);
        LoginAdmissionTransportConnection connection = new(inner, lease);

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => connection.DisposeAsync().AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(connectionFailure, exception.InnerExceptions[0]);
        Assert.Same(leaseFailure, exception.InnerExceptions[1]);
        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallsShareSingleDisposal()
    {
        TestTransportConnection inner = new();
        TestAdmissionLease lease = new();
        LoginAdmissionTransportConnection connection = new(inner, lease);

        Task[] disposals = Enumerable.Range(0, 16).Select(_ => connection.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals);

        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    private sealed class TestAdmissionLease(Exception? disposeFailure = null) : IAccountLoginConnectionLease
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);

            if (disposeFailure is not null)
            {
                throw disposeFailure;
            }
        }
    }

    private sealed class TestTransportConnection(byte[]? receiveBytes = null, Exception? disposeFailure = null) : ITransportConnection
    {
        private readonly byte[] _receiveBytes = receiveBytes ?? [];
        private readonly List<byte> _sentBytes = [];
        private int _disposeCount;

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 50000);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public byte[] SentBytes => [.. _sentBytes];

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _receiveBytes.CopyTo(buffer);
            return ValueTask.FromResult(_receiveBytes.Length);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sentBytes.AddRange(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);

            if (disposeFailure is not null)
            {
                return ValueTask.FromException(disposeFailure);
            }

            return ValueTask.CompletedTask;
        }
    }
}
