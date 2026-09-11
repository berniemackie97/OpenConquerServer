using System.Net;
using System.Runtime.ExceptionServices;
using OpenConquer.Infrastructure.Security.Accounts.Authentication;
using OpenConquer.Transport.Connections;

namespace OpenConquer.AccountServer.Login.Connections;

internal sealed class LoginAdmissionTransportConnection(ITransportConnection connection, IAccountLoginConnectionLease admissionLease)
    : ITransportConnection
{
    private readonly ITransportConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private readonly IAccountLoginConnectionLease _admissionLease = admissionLease ?? throw new ArgumentNullException(nameof(admissionLease));
    private readonly Lock _disposeGate = new();

    private Task? _disposeTask;

    public EndPoint LocalEndPoint => _connection.LocalEndPoint;
    public EndPoint RemoteEndPoint => _connection.RemoteEndPoint;

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _connection.ReceiveAsync(buffer, cancellationToken);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _connection.SendAsync(buffer, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? connectionDisposalFailure = null;

        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            connectionDisposalFailure = exception;
        }

        Exception? leaseDisposalFailure = null;

        try
        {
            _admissionLease.Dispose();
        }
        catch (Exception exception)
        {
            leaseDisposalFailure = exception;
        }

        if (connectionDisposalFailure is not null && leaseDisposalFailure is not null)
        {
            throw new AggregateException(
                "Disposing the login transport connection and releasing its admission lease both failed.",
                connectionDisposalFailure,
                leaseDisposalFailure
            );
        }

        Exception? failure = connectionDisposalFailure ?? leaseDisposalFailure;

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
