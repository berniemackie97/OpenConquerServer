using System.Net;
using System.Runtime.ExceptionServices;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Transport.Admission;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Login.Connections;

internal sealed class LoginAdmissionTransportListener(ITransportConnectionListener listener, IAccountLoginConnectionLimiter connectionLimiter, Action reportSourceRejection, Action<TransportConnectionRejectionDisposalFailure> reportRejectionDisposalFailure)
    : ITransportConnectionListener
{
    private readonly ITransportConnectionListener _listener = listener ?? throw new ArgumentNullException(nameof(listener));
    private readonly IAccountLoginConnectionLimiter _connectionLimiter = connectionLimiter ?? throw new ArgumentNullException(nameof(connectionLimiter));
    private readonly Action _reportSourceRejection = reportSourceRejection ?? throw new ArgumentNullException(nameof(reportSourceRejection));
    private readonly Action<TransportConnectionRejectionDisposalFailure> _reportRejectionDisposalFailure = reportRejectionDisposalFailure ?? throw new ArgumentNullException(nameof(reportRejectionDisposalFailure));

    public async ValueTask<ITransportConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ITransportConnection connection = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            IAccountLoginConnectionLease? admissionLease = null;

            try
            {
                if (connection.RemoteEndPoint is not IPEndPoint remoteEndPoint)
                {
                    throw new InvalidOperationException("The AccountServer login listener accepted a connection without an IP remote endpoint.");
                }

                if (_connectionLimiter.TryBeginConnection(remoteEndPoint.Address, out admissionLease))
                {
                    LoginAdmissionTransportConnection admittedConnection = new(connection, admissionLease);
                    admissionLease = null;
                    return admittedConnection;
                }
            }
            catch (Exception admissionFailure)
            {
                Exception? cleanupFailure = await DisposeUntransferredConnectionAsync(connection, admissionLease).ConfigureAwait(false);

                if (cleanupFailure is not null)
                {
                    throw new AggregateException("AccountServer login connection admission failed and cleanup also failed.", admissionFailure, cleanupFailure);
                }

                ExceptionDispatchInfo.Capture(admissionFailure).Throw();
                throw;
            }

            await DisposeRejectedConnectionAsync(connection).ConfigureAwait(false);
            ReportSourceRejection();

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public ValueTask DisposeAsync()
    {
        return _listener.DisposeAsync();
    }

    private async ValueTask DisposeRejectedConnectionAsync(ITransportConnection connection)
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
                _reportRejectionDisposalFailure(failure);
            }
            catch (Exception reportingException)
            {
                throw new AggregateException("Failed to report an AccountServer source-admission rejection disposal failure.", disposalException, reportingException);
            }
        }
    }

    private static async ValueTask<Exception?> DisposeUntransferredConnectionAsync(ITransportConnection connection, IAccountLoginConnectionLease? admissionLease)
    {
        Exception? connectionDisposalFailure = null;

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            connectionDisposalFailure = exception;
        }

        Exception? leaseDisposalFailure = null;

        try
        {
            admissionLease?.Dispose();
        }
        catch (Exception exception)
        {
            leaseDisposalFailure = exception;
        }

        if (connectionDisposalFailure is not null && leaseDisposalFailure is not null)
        {
            return new AggregateException("Disposing the untransferred login connection and releasing its admission lease both failed.", connectionDisposalFailure, leaseDisposalFailure);
        }

        return connectionDisposalFailure ?? leaseDisposalFailure;
    }

    private void ReportSourceRejection()
    {
        try
        {
            _reportSourceRejection();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Failed to report an AccountServer source-admission rejection.", exception);
        }
    }
}
