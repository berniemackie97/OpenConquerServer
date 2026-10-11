using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenConquer.Application.World;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Tests.Configuration;
using OpenConquer.GameServer.Tests.World;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.GameServer.Tests.Hosting;

public sealed class GameServerHostTests
{
    [Fact]
    public async Task Host_VerifiesBothDatabasesAndCanonicalContentBeforePublishingWorld()
    {
        ControlledReadiness account = new(blocked: true);
        ControlledReadiness game = new(blocked: true);
        ControlledContentRepositories content = new(blocked: true);
        IHost host = CreateHost(account, game, content);

        try
        {
            GameServerWorldState state = host.Services.GetRequiredService<GameServerWorldState>();

            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());

            Task startup = host.StartAsync(TestContext.Current.CancellationToken);

            await account.Started.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(startup.IsCompleted);
            Assert.Equal(0, game.Calls);
            Assert.Equal(0, content.DefinitionCalls);
            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());

            account.Release();

            await game.Started.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(startup.IsCompleted);
            Assert.Equal(1, game.Calls);
            Assert.Equal(0, content.DefinitionCalls);
            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());

            game.Release();

            await content.Started.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.False(startup.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());

            content.Release();

            await startup.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            (WorldMapContentCatalog maps, WorldRuntime runtime) = state.GetRequiredWorld();

            Assert.Equal(1, maps.Count);
            Assert.True(runtime.IsRunning);
            Assert.True(maps.TryGet(1002, out WorldMapContent? canonical));
            Assert.True(runtime.TryGetMap(1002, out WorldMapContent? runtimeMap));
            Assert.Same(canonical, runtimeMap);
            Assert.Equal(1, account.Calls);
            Assert.Equal(1, game.Calls);
            Assert.Equal(1, content.DefinitionCalls);

            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.False(runtime.IsRunning);
            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());
        }
        finally
        {
            account.Release();
            game.Release();
            content.Release();
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_AccountDatabaseFailurePreventsGameAndContentReadiness()
    {
        IOException failure = new("account schema unavailable");
        ControlledReadiness account = new(failure: failure);
        ControlledReadiness game = new();
        ControlledContentRepositories content = new();
        IHost host = CreateHost(account, game, content);

        try
        {
            IOException observed = await Assert.ThrowsAsync<IOException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Same(failure, observed);
            Assert.Equal(0, game.Calls);
            Assert.Equal(0, content.DefinitionCalls);
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_GameDatabaseFailurePreventsCanonicalContentLoading()
    {
        IOException failure = new("game schema unavailable");
        ControlledReadiness account = new();
        ControlledReadiness game = new(failure: failure);
        ControlledContentRepositories content = new();
        IHost host = CreateHost(account, game, content);

        try
        {
            IOException observed = await Assert.ThrowsAsync<IOException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Same(failure, observed);
            Assert.Equal(1, account.Calls);
            Assert.Equal(0, content.DefinitionCalls);
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_InvalidCanonicalContentPreventsWorldPublication()
    {
        InvalidDataException failure = new("canonical terrain is incompatible");
        ControlledReadiness account = new();
        ControlledReadiness game = new();
        ControlledContentRepositories content = new(failure: failure);
        IHost host = CreateHost(account, game, content);

        try
        {
            InvalidDataException observed = await Assert.ThrowsAsync<InvalidDataException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Same(failure, observed);
            Assert.Equal(1, account.Calls);
            Assert.Equal(1, game.Calls);
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_CancelledStartupNeverPublishesWorld()
    {
        ControlledReadiness account = new(blocked: true);
        ControlledReadiness game = new();
        ControlledContentRepositories content = new();
        IHost host = CreateHost(account, game, content);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        try
        {
            Task startup = host.StartAsync(cancellation.Token);

            await account.Started.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);

            Assert.Equal(0, game.Calls);
            Assert.Equal(0, content.DefinitionCalls);
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<GameServerWorldState>().GetRequiredWorld());
        }
        finally
        {
            account.Release();
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_UnexpectedWorldStopRecordsFatalFailureAndRequestsApplicationShutdown()
    {
        IHost host = CreateHost(new ControlledReadiness(), new ControlledReadiness(), new ControlledContentRepositories());

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);

            GameServerFailureState fatal = host.Services.GetRequiredService<GameServerFailureState>();
            GameServerWorldState state = host.Services.GetRequiredService<GameServerWorldState>();
            IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            WorldRuntime runtime = state.GetRequiredWorld().Runtime;
            TaskCompletionSource stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);

            using CancellationTokenRegistration registration = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());

            await runtime.StopAsync();

            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            InvalidOperationException failure = Assert.IsType<InvalidOperationException>(fatal.Failure);
            Assert.Contains("stopped unexpectedly", failure.Message, StringComparison.Ordinal);
            Assert.Null(failure.InnerException);

            await host.StopAsync(TestContext.Current.CancellationToken);

            Assert.Throws<InvalidOperationException>(() => state.GetRequiredWorld());
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [Fact]
    public async Task Host_FaultedWorldShutdownPreservesCauseAndReturnsFailureExitCode()
    {
        IHost host = CreateHost(new ControlledReadiness(), new ControlledReadiness(), new ControlledContentRepositories());
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        GameServerFailureState failureState = host.Services.GetRequiredService<GameServerFailureState>();
        GameServerWorldState worldState = host.Services.GetRequiredService<GameServerWorldState>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());

        Task<int> running = OpenConquer.GameServer.Program.RunAsync(host);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            WorldRuntime runtime = worldState.GetRequiredWorld().Runtime;
            IOException cause = new("world admission lifetime failed");

            runtime.TrackAdmission(Task.FromException(cause));

            IOException runtimeFailure = await Assert.ThrowsAsync<IOException>(
                () => runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

            Assert.Same(cause, runtimeFailure);
            Assert.Equal(1, await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

            InvalidOperationException failure = Assert.IsType<InvalidOperationException>(failureState.Failure);

            Assert.Contains("stopped unexpectedly", failure.Message, StringComparison.Ordinal);
            Assert.Same(cause, failure.InnerException);
            Assert.Throws<InvalidOperationException>(() => worldState.GetRequiredWorld());
        }
        finally
        {
            if (!running.IsCompleted)
            {
                lifetime.StopApplication();
            }

            await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Host_UnexpectedWorldStopRequestsShutdownWhenFatalLoggingFails()
    {
        IOException loggingFailure = new("fatal log sink failed");
        ThrowingFatalLogger logger = new(loggingFailure);
        IHost host = CreateHost(new ControlledReadiness(), new ControlledReadiness(), new ControlledContentRepositories(), logger);
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        GameServerFailureState failureState = host.Services.GetRequiredService<GameServerFailureState>();
        GameServerWorldState worldState = host.Services.GetRequiredService<GameServerWorldState>();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenRegistration registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());

        Task<int> running = OpenConquer.GameServer.Program.RunAsync(host);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            WorldRuntime runtime = worldState.GetRequiredWorld().Runtime;

            await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);

            Assert.Equal(1, await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
            Assert.Equal(1, logger.Calls);

            InvalidOperationException failure = Assert.IsType<InvalidOperationException>(failureState.Failure);

            Assert.Contains("stopped unexpectedly", failure.Message, StringComparison.Ordinal);
            Assert.Null(failure.InnerException);
            Assert.Throws<InvalidOperationException>(() => worldState.GetRequiredWorld());
        }
        finally
        {
            if (!running.IsCompleted)
            {
                lifetime.StopApplication();
            }

            await running.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Host_RegistersOneReadinessGatedWorldServiceAndStrictLifecycleOptions()
    {
        IHost host = CreateHost(new ControlledReadiness(), new ControlledReadiness(), new ControlledContentRepositories());

        try
        {
            IHostedService[] services = host.Services.GetServices<IHostedService>().ToArray();

            Assert.Collection(services, service => Assert.IsType<GameServerWorldHostedService>(service));

            HostOptions options = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;

            Assert.Equal(TimeSpan.FromMinutes(2), options.StartupTimeout);
            Assert.Equal(TimeSpan.FromMinutes(1), options.ShutdownTimeout);
            Assert.False(options.ServicesStartConcurrently);
            Assert.False(options.ServicesStopConcurrently);
            Assert.Equal(BackgroundServiceExceptionBehavior.StopHost, options.BackgroundServiceExceptionBehavior);

            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    private static IHost CreateHost(ControlledReadiness account, ControlledReadiness game, ControlledContentRepositories content,
        ILogger<GameServerWorldHostedService>? logger = null)
    {
        GameServerConfiguration configuration = GameServerConfiguration.Load(GameServerConfigurationTests.CreateConfiguration(), Path.GetTempPath());
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);

        GameServerHost.ConfigureServices(builder.Services, configuration);

        builder.Services.AddSingleton<IAccountDatabaseReadinessVerifier>(account);
        builder.Services.AddSingleton<IGameDatabaseReadinessVerifier>(game);
        builder.Services.AddSingleton<IMapDefinitionCatalogRepository>(content);
        builder.Services.AddSingleton<IMapBaseTerrainCatalogRepository>(content);
        builder.Services.AddSingleton<IMapTerrainCollisionCatalogRepository>(content);

        if (logger is not null)
        {
            builder.Services.AddSingleton(logger);
        }

        return builder.Build();
    }

    private static async ValueTask DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }
    }

    private sealed class ControlledReadiness : IAccountDatabaseReadinessVerifier, IGameDatabaseReadinessVerifier
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Exception? _failure;
        private int _calls;

        public ControlledReadiness(bool blocked = false, Exception? failure = null)
        {
            _failure = failure;

            if (!blocked)
            {
                Release();
            }
        }

        public Task Started => _started.Task;
        public int Calls => Volatile.Read(ref _calls);

        public async Task VerifyAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _started.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);

            if (_failure is not null)
            {
                throw _failure;
            }
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ControlledContentRepositories :
        IMapDefinitionCatalogRepository, IMapBaseTerrainCatalogRepository, IMapTerrainCollisionCatalogRepository
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WorldMapContent _map = WorldMapTestContent.Create(1002, 1015, 0);
        private readonly Exception? _failure;
        private int _definitionCalls;

        public ControlledContentRepositories(bool blocked = false, Exception? failure = null)
        {
            _failure = failure;

            if (!blocked)
            {
                Release();
            }
        }

        public Task Started => _started.Task;
        public int DefinitionCalls => Volatile.Read(ref _definitionCalls);

        public async ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _definitionCalls);
            _started.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);

            if (_failure is not null)
            {
                throw _failure;
            }

            return new MapDefinitionCatalog([_map.Definition]);
        }

        public ValueTask<MapBaseTerrainCatalog> LoadAsync(MapDefinitionCatalog definitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapBaseTerrainCatalog([_map.BaseTerrain]));
        }

        public ValueTask<MapTerrainCollisionCatalog> LoadAsync(MapDefinitionCatalog definitions, MapBaseTerrainCatalog terrains,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MapTerrainCollisionCatalog([_map.Collision]));
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ThrowingFatalLogger(Exception failure) : ILogger<GameServerWorldHostedService>
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Critical;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Interlocked.Increment(ref _calls);
            throw failure;
        }
    }
}
