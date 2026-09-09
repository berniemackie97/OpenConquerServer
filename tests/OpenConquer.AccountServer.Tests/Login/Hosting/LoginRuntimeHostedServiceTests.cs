using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Hosting;
using OpenConquer.AccountServer.Login.Observability;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Login.Hosting;

public sealed class LoginRuntimeHostedServiceTests
{
    [Fact]
    public async Task StartAsync_CreatesListenerOnlyDuringStartupAndStopDisposesIt()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using LoginRuntimeMetrics metrics = new();
        ControlledTransportConnectionListener listener = new();
        int factoryCalls = 0;

        using LoginRuntimeHostedService service = CreateService(() =>
        {
            factoryCalls++;
            return listener;
        }, queue, metrics);

        Assert.Equal(0, factoryCalls);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, factoryCalls);
        Assert.NotNull(service.ExecuteTask);
        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);

        await using IAsyncEnumerator<ITransportConnection> enumerator = queue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task StartAsync_RejectsMissingListenerFromFactory()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using LoginRuntimeMetrics metrics = new();
        using LoginRuntimeHostedService service = CreateService(static () => null!, queue, metrics);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("returned no listener", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteTask_PropagatesAcceptLoopFailureAndDisposesListenerOnStop()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using LoginRuntimeMetrics metrics = new();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics);

        await service.StartAsync(TestContext.Current.CancellationToken);

        IOException expected = new("accept loop failed");

        listener.Fail(expected);

        IOException actual = await Assert.ThrowsAsync<IOException>(() => service.ExecuteTask!);

        Assert.Same(expected, actual);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
    }

    [Fact]
    public async Task ExecuteTask_TreatsUnexpectedAdmissionCompletionAsRuntimeFailure()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using LoginRuntimeMetrics metrics = new();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics);

        await service.StartAsync(TestContext.Current.CancellationToken);

        queue.Complete();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTask!);

        Assert.Contains("terminated unexpectedly", exception.Message, StringComparison.Ordinal);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
    }

    [Fact]
    public async Task StopAsync_IsIdempotentForListenerOwnership()
    {
        await using TransportConnectionAdmissionQueue queue = new(capacity: 1);
        using LoginRuntimeMetrics metrics = new();
        ControlledTransportConnectionListener listener = new();
        using LoginRuntimeHostedService service = CreateService(() => listener, queue, metrics);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, listener.DisposeCallCount);
    }

    private static LoginRuntimeHostedService CreateService(LoginTransportListenerFactory listenerFactory, TransportConnectionAdmissionQueue queue, LoginRuntimeMetrics metrics)
    {
        GameLoginTicketIssuer ticketIssuer = new(new ThrowingGameLoginTicketGrantStore(), new TestGameLoginTicketTokenGenerator());
        LoginHandshakeProcessor handshakeProcessor = new(new ThrowingAccountAuthenticator(), ticketIssuer, new LoginHandshakeConfiguration(IPAddress.Loopback, 5816, TimeSpan.FromSeconds(5)));

        return new LoginRuntimeHostedService(
            listenerFactory,
            queue,
            new TestLoginSeedGenerator(),
            handshakeProcessor,
            new LoginConnectionWorkerPoolConfiguration(workerCount: 1, connectionTimeout: TimeSpan.FromSeconds(5)),
            metrics,
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
