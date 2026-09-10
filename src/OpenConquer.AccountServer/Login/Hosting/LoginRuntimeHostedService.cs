using System.Net;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Observability;
using OpenConquer.AccountServer.Login.Workers;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Login.Hosting;

internal delegate ITransportConnectionListener LoginTransportListenerFactory();

internal sealed partial class LoginRuntimeHostedService(LoginTransportListenerFactory listenerFactory, TransportConnectionAdmissionQueue admissionQueue, ILoginSeedGenerator seedGenerator, LoginHandshakeProcessor handshakeProcessor, LoginConnectionWorkerPoolConfiguration workerConfiguration, LoginRuntimeMetrics metrics, ILogger<LoginRuntimeHostedService> logger) : BackgroundService
{
    private readonly LoginTransportListenerFactory _listenerFactory = listenerFactory ?? throw new ArgumentNullException(nameof(listenerFactory));
    private readonly TransportConnectionAdmissionQueue _admissionQueue = admissionQueue ?? throw new ArgumentNullException(nameof(admissionQueue));
    private readonly ILoginSeedGenerator _seedGenerator = seedGenerator ?? throw new ArgumentNullException(nameof(seedGenerator));
    private readonly LoginHandshakeProcessor _handshakeProcessor = handshakeProcessor ?? throw new ArgumentNullException(nameof(handshakeProcessor));
    private readonly LoginConnectionWorkerPoolConfiguration _workerConfiguration = workerConfiguration ?? throw new ArgumentNullException(nameof(workerConfiguration));
    private readonly LoginRuntimeMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger<LoginRuntimeHostedService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TaskCompletionSource _runtimeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ITransportConnectionListener? _listener;
    private int _startState;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _startState, 1) != 0)
        {
            throw new InvalidOperationException("The account login runtime has already been started.");
        }

        try
        {
            _listener = _listenerFactory() ?? throw new InvalidOperationException("The account login listener factory returned no listener.");

            await base.StartAsync(cancellationToken).ConfigureAwait(false);

            Task executeTask = ExecuteTask ?? throw new InvalidOperationException("The account login runtime did not create a background execution task.");
            Task startupTask = _runtimeStarted.Task;
            Task completedTask = await Task.WhenAny(startupTask, executeTask).WaitAsync(cancellationToken).ConfigureAwait(false);

            if (ReferenceEquals(completedTask, executeTask))
            {
                await executeTask.ConfigureAwait(false);
                throw new InvalidOperationException("The account login runtime terminated during startup.");
            }

            await startupTask.ConfigureAwait(false);

            if (executeTask.IsCompleted)
            {
                await executeTask.ConfigureAwait(false);
                throw new InvalidOperationException("The account login runtime terminated during startup.");
            }

            LogRuntimeStarted(_logger);
        }
        catch (Exception startupException)
        {
            Exception? cleanupException = await StopRuntimeAsync(CancellationToken.None, logStopped: false).ConfigureAwait(false);

            if (cleanupException is not null)
            {
                throw new AggregateException("Account login runtime startup failed and cleanup also failed.", startupException, cleanupException);
            }

            ExceptionDispatchInfo.Capture(startupException).Throw();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = await StopRuntimeAsync(cancellationToken, logStopped: true).ConfigureAwait(false);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ITransportConnectionListener listener = _listener ?? throw new InvalidOperationException("The account login listener was not created before runtime execution.");

        try
        {
            using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            Task workerTask = LoginConnectionWorkerPool.RunAsync(_admissionQueue, _seedGenerator, _handshakeProcessor, _workerConfiguration, ReportConnectionTimeout, ReportConnectionFailure, runtimeCancellation.Token);
            Task acceptTask = TransportConnectionAcceptLoop.RunAsync(listener, _admissionQueue, _metrics.RecordCapacityRejection, ReportRejectionDisposalFailure, runtimeCancellation.Token);

            _runtimeStarted.TrySetResult();

            await SuperviseRuntimeAsync(acceptTask, workerTask, runtimeCancellation, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception runtimeException)
        {
            _runtimeStarted.TrySetException(runtimeException);

            Exception? listenerDisposalFailure = await DisposeListenerAsync().ConfigureAwait(false);

            if (listenerDisposalFailure is not null)
            {
                throw new AggregateException("The account login runtime failed and disposing its listener also failed.", runtimeException, listenerDisposalFailure);
            }

            ExceptionDispatchInfo.Capture(runtimeException).Throw();
        }
    }

    private async Task SuperviseRuntimeAsync(Task acceptTask, Task workerTask, CancellationTokenSource runtimeCancellation, CancellationToken stoppingToken)
    {
        Task completedTask = await Task.WhenAny(acceptTask, workerTask).ConfigureAwait(false);

        if (stoppingToken.IsCancellationRequested)
        {
            _admissionQueue.Complete();

            Exception? acceptFailure = await CaptureShutdownFailureAsync(acceptTask, runtimeCancellation.Token).ConfigureAwait(false);
            Exception? workerFailure = await CaptureShutdownFailureAsync(workerTask, runtimeCancellation.Token).ConfigureAwait(false);

            ThrowShutdownFailures(acceptFailure, workerFailure);
            return;
        }

        string unexpectedCompletionMessage = ReferenceEquals(completedTask, acceptTask)
            ? "The account login accept loop terminated unexpectedly."
            : "The account login worker pool terminated unexpectedly.";

        Exception primaryFailure = await CaptureUnexpectedCompletionAsync(completedTask, unexpectedCompletionMessage).ConfigureAwait(false);

        _admissionQueue.Complete();

        Exception? cancellationFailure = null;

        try
        {
            await runtimeCancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cancellationFailure = exception;
        }

        Task siblingTask = ReferenceEquals(completedTask, acceptTask) ? workerTask : acceptTask;
        Exception? siblingFailure = await CaptureShutdownFailureAsync(siblingTask, runtimeCancellation.Token).ConfigureAwait(false);

        ExceptionDispatchInfo.Capture(CombineRuntimeFailure(primaryFailure, cancellationFailure, siblingFailure)).Throw();
    }

    private async Task<Exception?> StopRuntimeAsync(CancellationToken cancellationToken, bool logStopped)
    {
        Exception? stopFailure = null;
        Exception? listenerDisposalFailure = null;

        if (ExecuteTask is { IsCompleted: false })
        {
            try
            {
                await base.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopFailure = exception;
            }
        }

        _admissionQueue.Complete();

        listenerDisposalFailure = await DisposeListenerAsync().ConfigureAwait(false);

        if (stopFailure is not null && listenerDisposalFailure is not null)
        {
            return new AggregateException("Stopping the account login runtime and disposing its listener both failed.", stopFailure, listenerDisposalFailure);
        }

        Exception? failure = stopFailure ?? listenerDisposalFailure;

        if (failure is null && logStopped && ExecuteTask is { IsCompletedSuccessfully: true })
        {
            LogRuntimeStopped(_logger);
        }

        return failure;
    }

    private static async Task<Exception> CaptureUnexpectedCompletionAsync(Task task, string unexpectedCompletionMessage)
    {
        try
        {
            await task.ConfigureAwait(false);
            return new InvalidOperationException(unexpectedCompletionMessage);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> CaptureShutdownFailureAsync(Task task, CancellationToken runtimeCancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (runtimeCancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception CombineRuntimeFailure(Exception primaryFailure, Exception? cancellationFailure, Exception? siblingFailure)
    {
        if (cancellationFailure is null && siblingFailure is null)
        {
            return primaryFailure;
        }

        List<Exception> failures = [primaryFailure];

        if (cancellationFailure is not null)
        {
            failures.Add(cancellationFailure);
        }

        if (siblingFailure is not null)
        {
            failures.Add(siblingFailure);
        }

        return new AggregateException("The account login runtime failed and one or more shutdown operations also failed.", failures);
    }

    private static void ThrowShutdownFailures(Exception? acceptFailure, Exception? workerFailure)
    {
        if (acceptFailure is null && workerFailure is null)
        {
            return;
        }

        if (acceptFailure is not null && workerFailure is not null)
        {
            throw new AggregateException("The account login runtime encountered multiple failures during shutdown.", acceptFailure, workerFailure);
        }

        ExceptionDispatchInfo.Capture(acceptFailure ?? workerFailure!).Throw();
    }

    private async Task<Exception?> DisposeListenerAsync()
    {
        ITransportConnectionListener? listener = Interlocked.Exchange(ref _listener, null);

        if (listener is null)
        {
            return null;
        }

        try
        {
            await listener.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void ReportConnectionTimeout(LoginConnectionProcessingTimeout timeout)
    {
        _metrics.RecordConnectionTimeout();
        LogConnectionTimeout(_logger, timeout.WorkerIndex, timeout.ConnectionTimeout, timeout.LocalEndPoint, timeout.RemoteEndPoint);
    }

    private void ReportConnectionFailure(LoginConnectionProcessingFailure failure)
    {
        _metrics.RecordConnectionFailure();
        LogConnectionFailure(_logger, failure.WorkerIndex, failure.LocalEndPoint, failure.RemoteEndPoint, failure.Exception);
    }

    private void ReportRejectionDisposalFailure(TransportConnectionRejectionDisposalFailure failure)
    {
        _metrics.RecordRejectionDisposalFailure();
        LogRejectionDisposalFailure(_logger, failure.LocalEndPoint, failure.RemoteEndPoint, failure.Exception);
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Account login runtime started.")]
    private static partial void LogRuntimeStarted(ILogger logger);
    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Account login runtime stopped.")]
    private static partial void LogRuntimeStopped(ILogger logger);
    [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message = "Login connection timed out after {ConnectionTimeout} on worker {WorkerIndex}. Local endpoint: {LocalEndPoint}; remote endpoint: {RemoteEndPoint}.")]
    private static partial void LogConnectionTimeout(ILogger logger, int workerIndex, TimeSpan connectionTimeout, EndPoint localEndPoint, EndPoint remoteEndPoint);
    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "Login connection processing failed on worker {WorkerIndex}. Local endpoint: {LocalEndPoint}; remote endpoint: {RemoteEndPoint}.")]
    private static partial void LogConnectionFailure(ILogger logger, int workerIndex, EndPoint localEndPoint, EndPoint remoteEndPoint, Exception exception);
    [LoggerMessage(EventId = 1104, Level = LogLevel.Error, Message = "Disposing an overload-rejected login connection failed. Local endpoint: {LocalEndPoint}; remote endpoint: {RemoteEndPoint}.")]
    private static partial void LogRejectionDisposalFailure(ILogger logger, EndPoint localEndPoint, EndPoint remoteEndPoint, Exception exception);
}
