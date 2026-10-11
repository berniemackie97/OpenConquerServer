using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.World;
using OpenConquer.GameServer.World.Execution;
using OpenConquer.Infrastructure.Persistence.Accounts.Readiness;
using OpenConquer.Infrastructure.Persistence.Game.Readiness;

namespace OpenConquer.GameServer.Hosting;

/// <summary>
/// Verifies required databases and canonical content before publishing and supervising authoritative world execution.
/// </summary>
internal sealed partial class GameServerWorldHostedService(
    IAccountDatabaseReadinessVerifier accountReadiness,
    IGameDatabaseReadinessVerifier gameReadiness,
    WorldMapContentLoader contentLoader,
    WorldExecutionOptions options,
    GameServerWorldState worldState,
    IHostApplicationLifetime applicationLifetime,
    FatalBackgroundServiceFailureState fatalFailureState,
    ILogger<GameServerWorldHostedService> logger) : IHostedService, IAsyncDisposable
{
    private readonly IAccountDatabaseReadinessVerifier _accountReadiness = accountReadiness ?? throw new ArgumentNullException(nameof(accountReadiness));
    private readonly IGameDatabaseReadinessVerifier _gameReadiness = gameReadiness ?? throw new ArgumentNullException(nameof(gameReadiness));
    private readonly WorldMapContentLoader _contentLoader = contentLoader ?? throw new ArgumentNullException(nameof(contentLoader));
    private readonly WorldExecutionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly GameServerWorldState _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
    private readonly FatalBackgroundServiceFailureState _fatalFailureState = fatalFailureState ?? throw new ArgumentNullException(nameof(fatalFailureState));
    private readonly ILogger<GameServerWorldHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Lock _gate = new();

    private WorldRuntime? _runtime;
    private Task? _monitor;
    private Task? _stopTask;
    private int _startState;
    private int _stopping;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _startState, 1) != 0)
        {
            throw new InvalidOperationException("The GameServer world host can only be started once.");
        }

        await _accountReadiness.VerifyAsync(cancellationToken).ConfigureAwait(false);
        LogAccountDatabaseReady(_logger);

        await _gameReadiness.VerifyAsync(cancellationToken).ConfigureAwait(false);
        LogGameDatabaseReady(_logger);

        WorldMapContentCatalog maps = await _contentLoader.LoadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        WorldRuntime runtime = new(maps, _options);

        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_stopping != 0)
                {
                    throw new InvalidOperationException("The GameServer world host stopped during startup.");
                }

                runtime.Start();

                _worldState.Publish(maps, runtime);
                _runtime = runtime;
                _monitor = ObserveRuntimeAsync(runtime, runtime.StoppingToken);
            }
        }
        catch (Exception startupFailure)
        {
            Exception? cleanupFailure = null;

            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (cleanupFailure is not null)
            {
                throw new AggregateException("GameServer world startup and cleanup both failed.", startupFailure, cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }

        LogWorldReady(_logger, maps.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task shutdown;

        lock (_gate)
        {
            if (_stopTask is null)
            {
                Volatile.Write(ref _stopping, 1);

                if (_runtime is not null)
                {
                    _worldState.Unpublish(_runtime);
                }

                _stopTask = Task.Run(StopCoreAsync);
            }

            shutdown = _stopTask;
        }

        return shutdown.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private async Task StopCoreAsync()
    {
        WorldRuntime? runtime = _runtime;
        Task? monitor = _monitor;

        if (runtime is null)
        {
            return;
        }

        Exception? shutdownFailure = null;

        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            shutdownFailure = exception;
        }

        if (monitor is not null)
        {
            try
            {
                await monitor.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                shutdownFailure = shutdownFailure is null
                    ? exception
                    : new AggregateException("GameServer world shutdown and supervision both failed.", shutdownFailure, exception);
            }
        }

        if (shutdownFailure is not null)
        {
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
        }

        LogWorldStopped(_logger);
    }

    private async Task ObserveRuntimeAsync(WorldRuntime runtime, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        InvalidOperationException failure = new("The authoritative world runtime stopped unexpectedly.");

        _fatalFailureState.Record(failure);
        LogUnexpectedWorldStop(_logger, failure);
        _applicationLifetime.StopApplication();
    }

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "GameServer account database readiness verified.")]
    private static partial void LogAccountDatabaseReady(ILogger logger);
    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "GameServer game database readiness verified.")]
    private static partial void LogGameDatabaseReady(ILogger logger);
    [LoggerMessage(EventId = 6002, Level = LogLevel.Information, Message = "Authoritative GameServer world started with {MapCount} canonical maps.")]
    private static partial void LogWorldReady(ILogger logger, int mapCount);
    [LoggerMessage(EventId = 6003, Level = LogLevel.Information, Message = "Authoritative GameServer world stopped.")]
    private static partial void LogWorldStopped(ILogger logger);
    [LoggerMessage(EventId = 6004, Level = LogLevel.Critical, Message = "Authoritative GameServer world terminated unexpectedly.")]
    private static partial void LogUnexpectedWorldStop(ILogger logger, Exception exception);
}
