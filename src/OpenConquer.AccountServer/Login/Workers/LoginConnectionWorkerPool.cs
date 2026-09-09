using System.Net;
using System.Runtime.ExceptionServices;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Login.Workers;

internal static class LoginConnectionWorkerPool
{
    public static async Task RunAsync(TransportConnectionAdmissionQueue admissionQueue, ILoginSeedGenerator seedGenerator, LoginHandshakeProcessor handshakeProcessor, LoginConnectionWorkerPoolConfiguration configuration, Action<LoginConnectionProcessingTimeout> reportTimeout, Action<LoginConnectionProcessingFailure> reportFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admissionQueue);
        ArgumentNullException.ThrowIfNull(seedGenerator);
        ArgumentNullException.ThrowIfNull(handshakeProcessor);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(reportTimeout);
        ArgumentNullException.ThrowIfNull(reportFailure);

        cancellationToken.ThrowIfCancellationRequested();

        using CancellationTokenSource poolCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<Exception?>[] workers = new Task<Exception?>[configuration.WorkerCount];

        for (int workerIndex = 0; workerIndex < workers.Length; workerIndex++)
        {
            workers[workerIndex] = RunWorkerGuardedAsync(workerIndex, admissionQueue, seedGenerator, handshakeProcessor, configuration, reportTimeout, reportFailure, poolCancellation);
        }

        Exception?[] workerFailures = await Task.WhenAll(workers).ConfigureAwait(false);
        List<Exception>? failures = null;

        foreach (Exception? failure in workerFailures)
        {
            if (failure is not null)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Multiple login connection workers failed.", failures);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<Exception?> RunWorkerGuardedAsync(int workerIndex, TransportConnectionAdmissionQueue admissionQueue, ILoginSeedGenerator seedGenerator, LoginHandshakeProcessor handshakeProcessor, LoginConnectionWorkerPoolConfiguration configuration, Action<LoginConnectionProcessingTimeout> reportTimeout, Action<LoginConnectionProcessingFailure> reportFailure, CancellationTokenSource poolCancellation)
    {
        try
        {
            await RunWorkerAsync(workerIndex, admissionQueue, seedGenerator, handshakeProcessor, configuration, reportTimeout, reportFailure, poolCancellation.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (poolCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception workerException)
        {
            try
            {
                await poolCancellation.CancelAsync().ConfigureAwait(false);
                return workerException;
            }
            catch (Exception cancellationException)
            {
                return new AggregateException("A login connection worker failed and canceling the worker pool also failed.", workerException, cancellationException);
            }
        }
    }

    private static async Task RunWorkerAsync(int workerIndex, TransportConnectionAdmissionQueue admissionQueue, ILoginSeedGenerator seedGenerator, LoginHandshakeProcessor handshakeProcessor, LoginConnectionWorkerPoolConfiguration configuration, Action<LoginConnectionProcessingTimeout> reportTimeout, Action<LoginConnectionProcessingFailure> reportFailure, CancellationToken cancellationToken)
    {
        await foreach (ITransportConnection connection in admissionQueue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await ProcessConnectionAsync(workerIndex, connection, seedGenerator, handshakeProcessor, configuration.ConnectionTimeout, reportTimeout, reportFailure, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ProcessConnectionAsync(int workerIndex, ITransportConnection connection, ILoginSeedGenerator seedGenerator, LoginHandshakeProcessor handshakeProcessor, TimeSpan connectionTimeout, Action<LoginConnectionProcessingTimeout> reportTimeout, Action<LoginConnectionProcessingFailure> reportFailure, CancellationToken cancellationToken)
    {
        EndPoint localEndPoint = connection.LocalEndPoint;
        EndPoint remoteEndPoint = connection.RemoteEndPoint;

        using CancellationTokenSource connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionCancellation.CancelAfter(connectionTimeout);

        LoginConnectionSession? session = null;
        Exception? processingException = null;
        Exception? disposalException = null;
        bool poolCanceled = false;
        bool timedOut = false;

        try
        {
            session = await LoginConnectionSession.OpenAsync(connection, seedGenerator, connectionCancellation.Token).ConfigureAwait(false);
            await handshakeProcessor.ProcessAsync(session, connectionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            processingException = exception;
            poolCanceled = true;
        }
        catch (OperationCanceledException exception) when (connectionCancellation.IsCancellationRequested)
        {
            processingException = exception;
            timedOut = true;
        }
        catch (Exception exception)
        {
            processingException = exception;
        }

        if (session is not null)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                disposalException = exception;
            }
        }

        if (poolCanceled)
        {
            if (disposalException is not null)
            {
                throw new AggregateException("Login connection processing was canceled and session cleanup also failed.", processingException!, disposalException);
            }

            ExceptionDispatchInfo.Capture(processingException!).Throw();
        }

        if (timedOut)
        {
            ReportTimeout(reportTimeout, new LoginConnectionProcessingTimeout(workerIndex, localEndPoint, remoteEndPoint, connectionTimeout), disposalException);

            if (disposalException is not null)
            {
                ReportFailure(reportFailure, new LoginConnectionProcessingFailure(workerIndex, localEndPoint, remoteEndPoint, disposalException));
            }

            return;
        }

        if (processingException is not null)
        {
            Exception failure = disposalException is null ? processingException : new AggregateException("Login connection processing failed and session cleanup also failed.", processingException, disposalException);
            ReportFailure(reportFailure, new LoginConnectionProcessingFailure(workerIndex, localEndPoint, remoteEndPoint, failure));
            return;
        }

        if (disposalException is not null)
        {
            ReportFailure(reportFailure, new LoginConnectionProcessingFailure(workerIndex, localEndPoint, remoteEndPoint, disposalException));
        }
    }

    private static void ReportTimeout(Action<LoginConnectionProcessingTimeout> reporter, LoginConnectionProcessingTimeout timeout, Exception? disposalException)
    {
        try
        {
            reporter(timeout);
        }
        catch (Exception reportingException)
        {
            if (disposalException is not null)
            {
                throw new AggregateException("Reporting a login connection timeout failed and session cleanup also failed.", disposalException, reportingException);
            }

            throw new InvalidOperationException("Reporting a login connection timeout failed.", reportingException);
        }
    }

    private static void ReportFailure(Action<LoginConnectionProcessingFailure> reporter, LoginConnectionProcessingFailure failure)
    {
        try
        {
            reporter(failure);
        }
        catch (Exception reportingException)
        {
            throw new AggregateException("Reporting a login connection processing failure failed.", failure.Exception, reportingException);
        }
    }
}
