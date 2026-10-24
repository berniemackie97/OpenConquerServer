using System.Buffers;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Application.Accounts.Registration;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake.Registration;

internal sealed class AccountRegistrationHandler(AccountRegistrationService? registration = null)
{
    private readonly AccountRegistrationService? _registration = registration;

    // A registration never authenticates, issues a ticket or reads post-authentication reports; the connection
    // closes after its one response.
    public async ValueTask ProcessAsync(LoginConnectionSession session, LoginAccountRegistrationRequest request,
        IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(remoteAddress);
        cancellationToken.ThrowIfCancellationRequested();

        if (_registration is null)
        {
            await SendRegistrationResultAsync(session, LoginAccountRegistrationResultCode.Unavailable, cancellationToken).ConfigureAwait(false);
            return;
        }

        char[] password = ArrayPool<char>.Shared.Rent(request.PasswordLength);
        AccountRegistrationResult result = default;
        Exception? failure = null;

        try
        {
            request.CopyPasswordTo(password);
            result = await _registration.RegisterAsync(request.AccountName, password.AsMemory(0, request.PasswordLength),
                remoteAddress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            failure = exception;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            ArrayPool<char>.Shared.Return(password);
            request.Dispose();
        }

        if (failure is not null)
        {
            try
            {
                await SendRegistrationResultAsync(session, LoginAccountRegistrationResultCode.Failed, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception responseFailure)
            {
                throw new AggregateException("Registration failed and its response could not be sent.", failure, responseFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        LoginAccountRegistrationResultCode code = result.Status switch
        {
            AccountRegistrationStatus.Created => LoginAccountRegistrationResultCode.Success,
            AccountRegistrationStatus.InvalidRequest => LoginAccountRegistrationResultCode.InvalidRequest,
            AccountRegistrationStatus.UsernameTaken => LoginAccountRegistrationResultCode.AccountNameTaken,
            AccountRegistrationStatus.Throttled => LoginAccountRegistrationResultCode.Unavailable,
            _ => throw new InvalidOperationException($"Account registration returned unsupported status {result.Status}."),
        };

        await SendRegistrationResultAsync(session, code, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask SendRegistrationResultAsync(LoginConnectionSession session, LoginAccountRegistrationResultCode code,
        CancellationToken cancellationToken)
    {
        return session.WriteAsync(new LoginAccountRegistrationResponsePacket(code), cancellationToken);
    }
}
