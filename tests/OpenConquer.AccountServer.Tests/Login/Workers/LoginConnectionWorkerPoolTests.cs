using System.Net;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Workers;

public sealed class LoginConnectionWorkerPoolTests
{
    [Fact]
    public async Task RunAsync_ReportsConnectionFailuresAndContinuesWorker()
    {
        await using TransportConnectionAdmissionQueue queue = new(2);
        IOException firstException = new("first connection failure");
        IOException secondException = new("second connection failure");
        TestTransportConnection first = TestTransportConnection.Failing(firstException, 41001);
        TestTransportConnection second = TestTransportConnection.Failing(secondException, 41002);
        List<LoginConnectionProcessingFailure> failures = [];

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(first));
        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(second));
        queue.Complete();

        await LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromSeconds(5)), _ => throw new InvalidOperationException("No timeout expected."), failures.Add, TestContext.Current.CancellationToken);

        Assert.Equal(2, failures.Count);
        Assert.Equal(0, failures[0].WorkerIndex);
        Assert.Equal(0, failures[1].WorkerIndex);
        Assert.Same(firstException, failures[0].Exception);
        Assert.Same(secondException, failures[1].Exception);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_ReportsWholeConnectionTimeout()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, 41003);
        List<LoginConnectionProcessingTimeout> timeouts = [];
        List<LoginConnectionProcessingFailure> failures = [];

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        await LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromMilliseconds(200)), timeouts.Add, failures.Add, TestContext.Current.CancellationToken);

        LoginConnectionProcessingTimeout timeout = Assert.Single(timeouts);
        Assert.Equal(0, timeout.WorkerIndex);
        Assert.Equal(connection.LocalEndPoint, timeout.LocalEndPoint);
        Assert.Equal(connection.RemoteEndPoint, timeout.RemoteEndPoint);
        Assert.Equal(TimeSpan.FromMilliseconds(200), timeout.ConnectionTimeout);
        Assert.Empty(failures);
        Assert.True(receiveStarted.Task.IsCompleted);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_UsesConfiguredWorkersConcurrently()
    {
        await using TransportConnectionAdmissionQueue queue = new(2);
        TaskCompletionSource firstReceiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondReceiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestTransportConnection first = TestTransportConnection.Blocking(firstReceiveStarted, 41004);
        TestTransportConnection second = TestTransportConnection.Blocking(secondReceiveStarted, 41005);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(first));
        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(second));

        Task workers = LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(2, TimeSpan.FromSeconds(30)), _ => throw new InvalidOperationException("No timeout expected."), _ => throw new InvalidOperationException("No failure expected."), cancellation.Token);

        await Task.WhenAll(firstReceiveStarted.Task, secondReceiveStarted.Task).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await workers.ConfigureAwait(false));

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_PropagatesCallerCancellationWithoutReportingClientFailure()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, 41006);
        List<LoginConnectionProcessingTimeout> timeouts = [];
        List<LoginConnectionProcessingFailure> failures = [];
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));

        Task workers = LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromSeconds(30)), timeouts.Add, failures.Add, cancellation.Token);

        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await workers.ConfigureAwait(false));

        Assert.Empty(timeouts);
        Assert.Empty(failures);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_ReporterFailureFaultsPool()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        IOException processingException = new("connection failure");
        InvalidOperationException reportingException = new("reporter failure");
        TestTransportConnection connection = TestTransportConnection.Failing(processingException, 41007);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromSeconds(5)), _ => { }, _ => throw reportingException, TestContext.Current.CancellationToken));

        Assert.Contains(processingException, exception.Flatten().InnerExceptions);
        Assert.Contains(reportingException, exception.Flatten().InnerExceptions);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_PreservesProcessingAndCleanupFailures()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IOException disposalException = new("dispose failure");
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, new DnsEndPoint("localhost", 41008), disposalException);
        List<LoginConnectionProcessingFailure> failures = [];

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        await LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromSeconds(5)), _ => throw new InvalidOperationException("No timeout expected."), failures.Add, TestContext.Current.CancellationToken);

        LoginConnectionProcessingFailure failure = Assert.Single(failures);
        AggregateException aggregate = Assert.IsType<AggregateException>(failure.Exception);
        IReadOnlyCollection<Exception> innerExceptions = aggregate.Flatten().InnerExceptions;

        Assert.Contains(innerExceptions, exception => exception is InvalidOperationException { Message: "Account login authentication requires an IP network endpoint." });
        Assert.Contains(disposalException, innerExceptions);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_ReportsTimeoutAndCleanupFailure()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IOException disposalException = new("dispose failure");
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, 41009, disposalException);
        List<LoginConnectionProcessingTimeout> timeouts = [];
        List<LoginConnectionProcessingFailure> failures = [];

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        await LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromMilliseconds(200)), timeouts.Add, failures.Add, TestContext.Current.CancellationToken);

        Assert.Single(timeouts);
        LoginConnectionProcessingFailure failure = Assert.Single(failures);
        Assert.Contains(disposalException, Assert.IsType<AggregateException>(failure.Exception).Flatten().InnerExceptions);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_TimeoutReporterFailureFaultsPool()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidOperationException reportingException = new("timeout reporter failure");
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, 41010);

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromMilliseconds(200)), _ => throw reportingException, _ => throw new InvalidOperationException("No processing failure expected."), TestContext.Current.CancellationToken));

        Assert.Equal("Reporting a login connection timeout failed.", exception.Message);
        Assert.Same(reportingException, exception.InnerException);
        Assert.Equal(1, connection.DisposeCount);
    }

    [Fact]
    public async Task RunAsync_ReportsCleanupFailureAfterNormalProcessing()
    {
        await using TransportConnectionAdmissionQueue queue = new(1);
        TaskCompletionSource receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IOException disposalException = new("dispose failure");
        TestTransportConnection connection = TestTransportConnection.Blocking(receiveStarted, 41011, disposalException);
        List<LoginConnectionProcessingFailure> failures = [];

        Assert.Equal(TransportConnectionAdmissionResult.Admitted, queue.TryAdmit(connection));
        queue.Complete();

        await LoginConnectionWorkerPool.RunAsync(queue, new FixedSeedGenerator(), CreateProcessor(TimeSpan.FromMilliseconds(100)), new LoginConnectionWorkerPoolConfiguration(1, TimeSpan.FromSeconds(5)), _ => throw new InvalidOperationException("No timeout expected."), failures.Add, TestContext.Current.CancellationToken);

        LoginConnectionProcessingFailure failure = Assert.Single(failures);
        Assert.Contains(disposalException, Assert.IsType<AggregateException>(failure.Exception).Flatten().InnerExceptions);
        Assert.Equal(1, connection.DisposeCount);
    }

    private static LoginHandshakeProcessor CreateProcessor(TimeSpan? phaseTimeout = null)
    {
        GameLoginTicketIssuer ticketIssuer = new(new UnusedGrantStore(), new UnusedTokenGenerator());
        return new LoginHandshakeProcessor(new UnexpectedAuthenticator(), ticketIssuer, new LoginHandshakeConfiguration(IPAddress.Loopback, 5816, phaseTimeout ?? TimeSpan.FromSeconds(10)));
    }

    private sealed class FixedSeedGenerator : ILoginSeedGenerator
    {
        public uint GenerateSeed() => 0x1234_5678;
    }

    private sealed class UnexpectedAuthenticator : IAccountAuthenticator
    {
        public ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Authentication was not expected.");
    }

    private sealed class UnusedGrantStore : IGameLoginTicketGrantStore
    {
        public ValueTask<GameLoginTicketGrantResult> TryGrantAsync(GameLoginTicketGrantRequest request, TimeSpan ticketLifetime, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Ticket persistence was not expected.");
    }

    private sealed class UnusedTokenGenerator : IGameLoginTicketTokenGenerator
    {
        public uint GenerateSessionUid() => throw new InvalidOperationException("Ticket generation was not expected.");
        public uint GenerateAuthenticationKey() => throw new InvalidOperationException("Ticket generation was not expected.");
    }

    private sealed class TestTransportConnection : ITransportConnection
    {
        private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>> _receive;
        private readonly Exception? _disposalException;
        private int _disposeCount;

        private TestTransportConnection(Func<Memory<byte>, CancellationToken, ValueTask<int>> receive, EndPoint remoteEndPoint, Exception? disposalException)
        {
            _receive = receive;
            _disposalException = disposalException;
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 9958);
            RemoteEndPoint = remoteEndPoint;
        }

        public EndPoint LocalEndPoint { get; }
        public EndPoint RemoteEndPoint { get; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public static TestTransportConnection Failing(Exception exception, int remotePort) => new((_, _) => ValueTask.FromException<int>(exception), new IPEndPoint(IPAddress.Loopback, remotePort), disposalException: null);

        public static TestTransportConnection Blocking(TaskCompletionSource receiveStarted, int remotePort, Exception? disposalException = null) => Blocking(receiveStarted, new IPEndPoint(IPAddress.Loopback, remotePort), disposalException);

        public static TestTransportConnection Blocking(TaskCompletionSource receiveStarted, EndPoint remoteEndPoint, Exception? disposalException = null)
        {
            return new TestTransportConnection(async (_, cancellationToken) =>
            {
                receiveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }, remoteEndPoint, disposalException);
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _receive(buffer, cancellationToken);

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);

            if (_disposalException is not null)
            {
                return ValueTask.FromException(_disposalException);
            }

            return ValueTask.CompletedTask;
        }
    }
}
