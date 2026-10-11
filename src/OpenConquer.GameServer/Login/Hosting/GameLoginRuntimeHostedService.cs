using System.Net;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.Application.Items.Catalog;
using OpenConquer.GameServer.Configuration;
using OpenConquer.GameServer.Hosting;
using OpenConquer.GameServer.Login.Admission;
using OpenConquer.GameServer.Login.Observability;
using OpenConquer.Infrastructure.Content.Items;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Login.Hosting;

/// <summary>
/// Starts bounded network ingress after canonical content readiness and owns all connection-processing work.
/// </summary>
internal sealed partial class GameLoginRuntimeHostedService(
    Func<ITransportConnectionListener> listenerFactory,
    Func<GameLoginConnectionProcessor> processorFactory,
    IItemTypeCatalogRepository itemRepository,
    GameServerItemCatalogState itemState,
    GameNetworkOptions options,
    TransportConnectionAdmissionQueue queue,
    GameLoginMetrics metrics,
    GameServerFailureState failureState,
    IHostApplicationLifetime applicationLifetime,
    ILoggerFactory loggerFactory,
    ILogger<GameLoginRuntimeHostedService> logger) : BackgroundService
{
    private readonly Func<ITransportConnectionListener> _listenerFactory = listenerFactory ?? throw new ArgumentNullException(nameof(listenerFactory));
    private readonly Func<GameLoginConnectionProcessor> _processorFactory = processorFactory ?? throw new ArgumentNullException(nameof(processorFactory));
    private readonly IItemTypeCatalogRepository _itemRepository = itemRepository ?? throw new ArgumentNullException(nameof(itemRepository));
    private readonly GameServerItemCatalogState _itemState = itemState ?? throw new ArgumentNullException(nameof(itemState));
    private readonly GameNetworkOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TransportConnectionAdmissionQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly GameLoginMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly GameServerFailureState _failureState = failureState ?? throw new ArgumentNullException(nameof(failureState));
    private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly ILogger<GameLoginRuntimeHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private GameAdmissionTransportListener? _listener;
    private GameConnectionWorkerPool? _workers;
    private int _startState;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _startState, 1) != 0)
        {
            throw new InvalidOperationException("The GameServer login runtime can only be started once.");
        }

        ItemTypeCatalog items = await _itemRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        _itemState.Publish(items);

        try
        {
            GameLoginConnectionProcessor processor = _processorFactory();

            _workers = new GameConnectionWorkerPool(_queue, processor.ProcessAsync, _options, _metrics,
                _failureState, _applicationLifetime, _loggerFactory.CreateLogger<GameConnectionWorkerPool>());

            ITransportConnectionListener transport = _listenerFactory()
                ?? throw new InvalidOperationException("The GameServer login listener factory returned no listener.");

            try
            {
                _listener = new GameAdmissionTransportListener(transport, _options.MaximumConnectionsPerSource,
                    _metrics, ReportFatalTransportDisposalFailure);
            }
            catch (Exception initializationFailure)
            {
                try
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("GameServer listener initialization and cleanup both failed.", initializationFailure, cleanupFailure);
                }

                throw;
            }

            await base.StartAsync(cancellationToken).ConfigureAwait(false);

            Task execution = ExecuteTask ?? throw new InvalidOperationException("The GameServer login runtime did not start.");
            Task first = await Task.WhenAny(_started.Task, execution).WaitAsync(cancellationToken).ConfigureAwait(false);

            if (ReferenceEquals(first, execution))
            {
                await execution.ConfigureAwait(false);
                throw new InvalidOperationException("The GameServer login runtime terminated during startup.");
            }

            await _started.Task.ConfigureAwait(false);

            if (execution.IsCompleted)
            {
                await execution.ConfigureAwait(false);
                throw new InvalidOperationException("The GameServer login runtime terminated during startup.");
            }

            LogRuntimeStarted(_logger, _options.EndPoint);
        }
        catch (Exception startupFailure)
        {
            List<Exception> failures = [startupFailure];

            if (ExecuteTask is not null)
            {
                try
                {
                    await base.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            else
            {
                _queue.Complete();

                if (_listener is not null)
                {
                    try
                    {
                        await _listener.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }

                try
                {
                    await _queue.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("GameServer login startup and cleanup both failed.", failures);
            }

            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        LogRuntimeStopped(_logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GameAdmissionTransportListener listener = _listener
            ?? throw new InvalidOperationException("GameServer listener is unavailable.");

        GameConnectionWorkerPool workers = _workers
            ?? throw new InvalidOperationException("GameServer connection workers are unavailable.");

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? acceptTask = null;
        Task? workerTask = null;
        List<Exception> failures = [];

        try
        {
            acceptTask = TransportConnectionAcceptLoop.RunAsync(listener, _queue,
                _metrics.RecordCapacityRejected, ReportRejectionDisposalFailure, cancellation.Token);

            workerTask = workers.RunAsync(cancellation.Token);
            _started.TrySetResult();

            Task first = await Task.WhenAny(acceptTask, workerTask).ConfigureAwait(false);

            if (!stoppingToken.IsCancellationRequested)
            {
                await first.ConfigureAwait(false);
                throw new InvalidOperationException("The GameServer listener or login worker pool terminated unexpectedly.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            _started.TrySetException(exception);
        }

        _queue.Complete();

        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (workerTask is not null)
        {
            try
            {
                await workerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            await _queue.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 0)
        {
            return;
        }

        Exception failure = failures.Count == 1
            ? failures[0]
            : new AggregateException("GameServer network execution and cleanup encountered multiple failures.", failures);

        _failureState.Record(failure);

        Exception? reportingFailure = null;

        try
        {
            LogFatalRuntimeFailure(_logger, failure);
        }
        catch (Exception exception)
        {
            reportingFailure = exception;
        }

        if (reportingFailure is not null)
        {
            throw new AggregateException("The GameServer runtime and fatal error reporting both failed.", failure, reportingFailure);
        }

        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ReportFatalTransportDisposalFailure(Exception failure)
    {
        _failureState.Record(failure);

        try
        {
            LogFatalTransportDisposal(_logger, failure);
        }
        finally
        {
            _applicationLifetime.StopApplication();
        }
    }

    private void ReportRejectionDisposalFailure(TransportConnectionRejectionDisposalFailure failure)
    {
        _failureState.Record(failure.Exception);

        try
        {
            LogRejectionDisposalFailure(_logger, failure.LocalEndPoint, failure.RemoteEndPoint, failure.Exception);
        }
        finally
        {
            _applicationLifetime.StopApplication();
        }
    }

    [LoggerMessage(EventId = 6100, Level = LogLevel.Information, Message = "GameServer login listener started on {EndPoint}.")]
    private static partial void LogRuntimeStarted(ILogger logger, IPEndPoint endPoint);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Information, Message = "GameServer login runtime stopped.")]
    private static partial void LogRuntimeStopped(ILogger logger);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Error, Message = "Disposing a capacity-rejected GameServer transport failed. Local: {LocalEndPoint}; remote: {RemoteEndPoint}.")]
    private static partial void LogRejectionDisposalFailure(ILogger logger, EndPoint localEndPoint, EndPoint remoteEndPoint, Exception exception);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Critical, Message = "GameServer network runtime terminated with a fatal failure.")]
    private static partial void LogFatalRuntimeFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6104, Level = LogLevel.Critical, Message = "GameServer transport disposal failed; connection closure cannot be guaranteed.")]
    private static partial void LogFatalTransportDisposal(ILogger logger, Exception exception);
}
