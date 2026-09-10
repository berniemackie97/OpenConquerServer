using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenConquer.AccountServer.Configuration;
using OpenConquer.AccountServer.Hosting;
using OpenConquer.AccountServer.Login.Hosting;
using OpenConquer.AccountServer.Maintenance;
using OpenConquer.AccountServer.Tests.Configuration;
using OpenConquer.Infrastructure.Persistence.Accounts.GameLogin;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Tests.Hosting;

public sealed class AccountServerHostTests
{
    [Fact]
    public async Task Host_DoesNotExposeLoginListenerUntilDatabaseReadinessCompletes()
    {
        AccountServerConfiguration configuration = AccountServerConfiguration.Load(AccountServerTestConfiguration.Create());
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        ControlledReadinessVerifier readiness = new();
        NoOpExpirationCleaner cleaner = new();
        ControlledTransportConnectionListener listener = new();
        int listenerFactoryCalls = 0;

        AccountServerHost.ConfigureServices(builder.Services, configuration);

        builder.Services.AddSingleton<IAccountDatabaseReadinessVerifier>(readiness);
        builder.Services.AddSingleton<IGameLoginTicketExpirationCleaner>(cleaner);
        builder.Services.AddSingleton<LoginTransportListenerFactory>(_ => () =>
        {
            Interlocked.Increment(ref listenerFactoryCalls);
            return listener;
        });

        IHost host = builder.Build();

        try
        {
            IHostedService[] hostedServices = host.Services.GetServices<IHostedService>().ToArray();

            Assert.Collection(
                hostedServices,
                service => Assert.IsType<AccountDatabaseReadinessHostedService>(service),
                service => Assert.IsType<GameLoginTicketCleanupHostedService>(service),
                service => Assert.IsType<LoginRuntimeHostedService>(service));

            Assert.Equal(0, Volatile.Read(ref listenerFactoryCalls));
            Assert.Null(host.Services.GetService<AccountServerConfiguration>());

            HostOptions hostOptions = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;

            Assert.Equal(TimeSpan.FromSeconds(30), hostOptions.StartupTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), hostOptions.ShutdownTimeout);
            Assert.False(hostOptions.ServicesStartConcurrently);
            Assert.False(hostOptions.ServicesStopConcurrently);
            Assert.Equal(BackgroundServiceExceptionBehavior.StopHost, hostOptions.BackgroundServiceExceptionBehavior);

            Task startup = host.StartAsync(TestContext.Current.CancellationToken);

            await readiness.Started.WaitAsync(TestContext.Current.CancellationToken);

            Assert.False(startup.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref listenerFactoryCalls));

            readiness.Complete();

            await startup.WaitAsync(TestContext.Current.CancellationToken);
            await listener.AcceptStarted.WaitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, Volatile.Read(ref listenerFactoryCalls));

            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.DisposeCallCount);
        }
        finally
        {
            await DisposeHostAsync(host);
        }

        Assert.Equal(1, listener.DisposeCallCount);
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        host.Dispose();
    }

    private sealed class ControlledReadinessVerifier : IAccountDatabaseReadinessVerifier
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class NoOpExpirationCleaner : IGameLoginTicketExpirationCleaner
    {
        public int MaximumBatchSize => 1_000;

        public ValueTask<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }

    private sealed class ControlledTransportConnectionListener : ITransportConnectionListener
    {
        private readonly TaskCompletionSource _acceptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCallCount;

        public Task AcceptStarted => _acceptStarted.Task;
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            _acceptStarted.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("The controlled listener completed without cancellation.");
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }
}
