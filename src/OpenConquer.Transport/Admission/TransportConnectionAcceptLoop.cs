using System.Net;
using OpenConquer.Transport.Connections;

namespace OpenConquer.Transport.Admission;

public static class TransportConnectionAcceptLoop
{
    public static Task RunAsync(ITransportConnectionListener listener, TransportConnectionAdmissionQueue admissionQueue, Action<TransportConnectionRejectionDisposalFailure> reportRejectionDisposalFailure, CancellationToken cancellationToken = default)
    {
        return RunAsync(listener, admissionQueue, static () => { }, reportRejectionDisposalFailure, cancellationToken);
    }

    public static async Task RunAsync(ITransportConnectionListener listener, TransportConnectionAdmissionQueue admissionQueue, Action reportCapacityRejection, Action<TransportConnectionRejectionDisposalFailure> reportRejectionDisposalFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(admissionQueue);
        ArgumentNullException.ThrowIfNull(reportCapacityRejection);
        ArgumentNullException.ThrowIfNull(reportRejectionDisposalFailure);

        while (true)
        {
            if (admissionQueue.AdmissionCompleted.IsCompleted)
            {
                return;
            }

            using CancellationTokenSource acceptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task<ITransportConnection> acceptTask = listener.AcceptAsync(acceptCancellation.Token).AsTask();
            Task completedTask = await Task.WhenAny(acceptTask, admissionQueue.AdmissionCompleted).ConfigureAwait(false);

            ITransportConnection connection;

            if (completedTask == acceptTask)
            {
                connection = await acceptTask.ConfigureAwait(false);
            }
            else
            {
                await acceptCancellation.CancelAsync().ConfigureAwait(false);

                try
                {
                    connection = await acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (acceptCancellation.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return;
                }

                await connection.DisposeAsync().ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
            }

            TransportConnectionAdmissionResult result = admissionQueue.TryAdmit(connection);

            switch (result)
            {
                case TransportConnectionAdmissionResult.Admitted:
                    break;

                case TransportConnectionAdmissionResult.CapacityExhausted:
                    await DisposeOverloadRejectedConnectionAsync(connection, reportRejectionDisposalFailure).ConfigureAwait(false);
                    ReportCapacityRejection(reportCapacityRejection);
                    break;

                case TransportConnectionAdmissionResult.Completed:
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return;

                default:
                    await connection.DisposeAsync().ConfigureAwait(false);

                    throw new InvalidOperationException($"Unexpected transport admission result '{result}'.");
            }
        }
    }

    private static void ReportCapacityRejection(Action reporter)
    {
        try
        {
            reporter();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Failed to report a transport connection capacity rejection.", exception);
        }
    }

    private static async ValueTask DisposeOverloadRejectedConnectionAsync(ITransportConnection connection, Action<TransportConnectionRejectionDisposalFailure> reportRejectionDisposalFailure)
    {
        EndPoint localEndPoint = connection.LocalEndPoint;
        EndPoint remoteEndPoint = connection.RemoteEndPoint;

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposalException)
        {
            TransportConnectionRejectionDisposalFailure failure = new(localEndPoint, remoteEndPoint, disposalException);

            try
            {
                reportRejectionDisposalFailure(failure);
            }
            catch (Exception reportingException)
            {
                throw new AggregateException("Failed to report a transport connection rejection disposal failure.", disposalException, reportingException);
            }
        }
    }
}
