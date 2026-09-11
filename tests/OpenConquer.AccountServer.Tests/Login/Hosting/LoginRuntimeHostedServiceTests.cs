using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.AccountServer.Hosting;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Hosting;
using OpenConquer.AccountServer.Login.Observability;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Hosting;

public sealed class LoginRuntimeHostedServiceTests
{
    [Fact]
    public async Task StartAsync_CreatesListenerOnlyDuringStartupAndStopDisposesIt()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        ControlledTransportConnectionListener listener = new();
        int factoryCalls = 0;

        using LoginRuntimeHostedService service = CreateService(() =>
        {
            factoryCalls++;
            return listener;
        }, queue, metrics, out FatalBackgroundServiceFailureState fatalFailureState);

        Assert.Equal(0, factoryCalls);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, factoryCalls);
        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, listener.DisposeCallCount);
        Assert.Null(fatalFailureState.Failure);

        await using IAsyncEnumerator<ITransportConnection> enumerator = queue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task StartAsync_RejectsMissingListenerFromFactory()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        using LoginRuntimeHostedService service = CreateService(static () => null!, queue, metrics);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("returned no listener", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteTask_PropagatesAcceptLoopFailureAndImmediatelyDisposesListener()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics, out FatalBackgroundServiceFailureState fatalFailureState);

        await service.StartAsync(TestContext.Current.CancellationToken);

        IOException expected = new("accept loop failed");

        listener.Fail(expected);

        IOException actual = await Assert.ThrowsAsync<IOException>(() => service.ExecuteTask!);

        Assert.Same(expected, actual);
        Assert.Same(actual, fatalFailureState.Failure);
        Assert.Equal(1, listener.DisposeCallCount);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
        Assert.Same(actual, fatalFailureState.Failure);
    }

    [Fact]
    public async Task ExecuteTask_TreatsUnexpectedAdmissionCompletionAsRuntimeFailure()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics, out FatalBackgroundServiceFailureState fatalFailureState);

        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Complete();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTask!);

        Assert.Contains("terminated unexpectedly", exception.Message, StringComparison.Ordinal);
        Assert.Same(exception, fatalFailureState.Failure);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
        Assert.Same(exception, fatalFailureState.Failure);
    }

    [Fact]
    public async Task ExecuteTask_SourceRejectedConnectionIsDisposedBeforeWorkerProcessing()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        TrackingTransportConnection connection = new();
        SingleConnectionThenBlockingListener listener = new(connection);
        RejectingConnectionLimiter limiter = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics, out FatalBackgroundServiceFailureState fatalFailureState, limiter);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await connection.Disposed.WaitAsync(TestContext.Current.CancellationToken);
        await listener.SecondAcceptStarted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, limiter.CallCount);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), limiter.LastRemoteAddress);
        Assert.Equal(1, connection.DisposeCallCount);
        Assert.Equal(0, connection.ReceiveCallCount);
        Assert.False(service.ExecuteTask!.IsCompleted);
        Assert.Null(fatalFailureState.Failure);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, listener.DisposeCallCount);
        Assert.Equal(1, connection.DisposeCallCount);
        Assert.Equal(0, connection.ReceiveCallCount);
        Assert.Null(fatalFailureState.Failure);
    }

    [Fact]
    public async Task StopAsync_IsIdempotentForListenerOwnership()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using ServiceProvider services = CreateMetricServices();
        LoginRuntimeMetrics metrics = services.GetRequiredService<LoginRuntimeMetrics>();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics, out FatalBackgroundServiceFailureState fatalFailureState);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
        Assert.Null(fatalFailureState.Failure);
    }

    private static ServiceProvider CreateMetricServices()
    {
        return new ServiceCollection().AddMetrics().AddSingleton<LoginRuntimeMetrics>().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static LoginRuntimeHostedService CreateService(LoginTransportListenerFactory listenerFactory, TransportConnectionAdmissionQueue queue, LoginRuntimeMetrics metrics, IAccountLoginConnectionLimiter? connectionLimiter = null)
    {
        return CreateService(listenerFactory, queue, metrics, out _, connectionLimiter);
    }

    private static LoginRuntimeHostedService CreateService(LoginTransportListenerFactory listenerFactory, TransportConnectionAdmissionQueue queue, LoginRuntimeMetrics metrics, out FatalBackgroundServiceFailureState fatalFailureState, IAccountLoginConnectionLimiter? connectionLimiter = null)
    {
        GameLoginTicketIssuer ticketIssuer = new(new ThrowingGameLoginTicketGrantStore(), new TestGameLoginTicketTokenGenerator());
        LoginHandshakeProcessor handshakeProcessor = new(new ThrowingAccountAuthenticator(), ticketIssuer, new LoginHandshakeConfiguration(IPAddress.Loopback, 5816, TimeSpan.FromSeconds(5)));
        fatalFailureState = new FatalBackgroundServiceFailureState();

        return new LoginRuntimeHostedService(
            listenerFactory,
            queue,
            connectionLimiter ?? new AllowingConnectionLimiter(),
            new TestLoginSeedGenerator(),
            handshakeProcessor,
            new LoginConnectionWorkerPoolConfiguration(workerCount: 1, connectionTimeout: TimeSpan.FromSeconds(5)),
            metrics,
            fatalFailureState,
            NullLogger<LoginRuntimeHostedService>.Instance);
    }

    private sealed class ControlledTransportConnectionListener : ITransportConnectionListener
    {
        private readonly TaskCompletionSource<ITransportConnection> _accept = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            return await _accept.Task.WaitAsync(cancellationToken);
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _accept.TrySetException(exception);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleConnectionThenBlockingListener(ITransportConnection connection) : ITransportConnectionListener
    {
        private readonly TaskCompletionSource _secondAcceptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptCallCount;
        private int _disposeCallCount;

        public Task SecondAcceptStarted => _secondAcceptStarted.Task;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Increment(ref _acceptCallCount) == 1)
            {
                return connection;
            }

            _secondAcceptStarted.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The blocking test listener completed without cancellation.");
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingTransportConnection : ITransportConnection
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;
        private int _receiveCallCount;

        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 9958);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 50000);
        public Task Disposed => _disposed.Task;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);
        public int ReceiveCallCount => Volatile.Read(ref _receiveCallCount);

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _receiveCallCount);
            return ValueTask.FromException<int>(new InvalidOperationException("A source-rejected connection must not reach a login worker."));
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new InvalidOperationException("A source-rejected connection must not reach a login worker."));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            _disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AllowingConnectionLimiter : IAccountLoginConnectionLimiter
    {
        public bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection)
        {
            ArgumentNullException.ThrowIfNull(remoteAddress);
            connection = new NoOpConnectionLease();
            return true;
        }
    }

    private sealed class RejectingConnectionLimiter : IAccountLoginConnectionLimiter
    {
        private int _callCount;
        private IPAddress? _lastRemoteAddress;

        public int CallCount => Volatile.Read(ref _callCount);
        public IPAddress? LastRemoteAddress => _lastRemoteAddress;

        public bool TryBeginConnection(IPAddress remoteAddress, [NotNullWhen(true)] out IAccountLoginConnectionLease? connection)
        {
            ArgumentNullException.ThrowIfNull(remoteAddress);

            _lastRemoteAddress = remoteAddress;
            Interlocked.Increment(ref _callCount);
            connection = null;
            return false;
        }
    }

    private sealed class NoOpConnectionLease : IAccountLoginConnectionLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class TestLoginSeedGenerator : ILoginSeedGenerator
    {
        public uint GenerateSeed() => 1;
    }

    private sealed class ThrowingAccountAuthenticator : IAccountAuthenticator
    {
        public ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<AccountAuthenticationResult>(new InvalidOperationException("Authentication should not be reached by runtime lifecycle tests."));
        }
    }

    private sealed class ThrowingGameLoginTicketGrantStore : IGameLoginTicketGrantStore
    {
        public ValueTask<GameLoginTicketGrantResult> TryGrantAsync(GameLoginTicketGrantRequest request, TimeSpan ticketLifetime, ulong expectedAccountStateRevision, ulong expectedPasswordCredentialRevision, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<GameLoginTicketGrantResult>(new InvalidOperationException("Ticket persistence should not be reached by runtime lifecycle tests."));
        }
    }

    private sealed class TestGameLoginTicketTokenGenerator : IGameLoginTicketTokenGenerator
    {
        public uint GenerateSessionUid() => 1;

        public uint GenerateAuthenticationKey() => 1;
    }
}
