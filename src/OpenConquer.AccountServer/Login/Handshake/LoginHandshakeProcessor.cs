using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

internal sealed class LoginHandshakeProcessor(IAccountAuthenticator authenticator, GameLoginTicketIssuer ticketIssuer, LoginHandshakeConfiguration configuration)
{
    private const ushort ProtectedAccountRequestPacketId = 1084;
    private const ushort MobileAccountRequestPacketId = 1098;

    private readonly IAccountAuthenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    private readonly GameLoginTicketIssuer _ticketIssuer = ticketIssuer ?? throw new ArgumentNullException(nameof(ticketIssuer));
    private readonly LoginHandshakeConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async ValueTask ProcessAsync(LoginConnectionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        IPAddress remoteAddress = session.RemoteEndPoint is IPEndPoint endpoint
            ? endpoint.Address
            : throw new InvalidOperationException("Account login authentication requires an IP network endpoint.");

        LoginAccountRequestReadResult? readResult = await ReadAccountRequestAsync(session, cancellationToken).ConfigureAwait(false);

        if (readResult is null || readResult.Status == LoginAccountRequestReadStatus.EndOfStream)
        {
            return;
        }

        if (readResult.Status == LoginAccountRequestReadStatus.UnexpectedPacket)
        {
            LoginAccountAuthenticationFailureCode failureCode = readResult.UnexpectedPacketId is ProtectedAccountRequestPacketId or MobileAccountRequestPacketId
                ? LoginAccountAuthenticationFailureCode.InvalidCredentials
                : LoginAccountAuthenticationFailureCode.InvalidAccount;

            await SendFailureAsync(session, failureCode, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (readResult.Status == LoginAccountRequestReadStatus.InvalidAccountRequest)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.InvalidAccount, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (readResult.Status != LoginAccountRequestReadStatus.Success)
        {
            throw new InvalidOperationException($"Account-request reader returned unsupported status {readResult.Status}.");
        }

        LoginAccountRequest request = readResult.Request ?? throw new InvalidOperationException("Account-request reader reported success without returning a request.");
        AccountAuthenticationResult authentication;

        using (request)
        {
            authentication = await AuthenticateAsync(request, remoteAddress, cancellationToken).ConfigureAwait(false);
        }

        if (authentication.Status == AccountAuthenticationStatus.InvalidCredentials)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.InvalidCredentials, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (authentication.Status == AccountAuthenticationStatus.Banned)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.Banned, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (authentication.Status != AccountAuthenticationStatus.Success)
        {
            throw new InvalidOperationException($"Account authenticator returned unsupported status {authentication.Status}.");
        }

        GameLoginTicket? ticket = await _ticketIssuer.IssueAsync(authentication, cancellationToken).ConfigureAwait(false);

        if (ticket is null)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.InvalidCredentials, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginAccountAuthenticationResponsePacket response = LoginAccountAuthenticationResponsePacket.Success(ticket.SessionUid, ticket.AuthenticationKey, _configuration.GameServerPort, ticket.AuthenticationKey, _configuration.GameServerIp);

        await session.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await ReadPostAuthenticationReportsAsync(session, ticket.SessionUid, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AccountAuthenticationResult> AuthenticateAsync(LoginAccountRequest request, IPAddress remoteAddress, CancellationToken cancellationToken)
    {
        char[] password = ArrayPool<char>.Shared.Rent(request.PasswordLength);

        try
        {
            request.CopyPasswordTo(password);

            return await _authenticator.AuthenticateAsync(request.AccountName, password.AsMemory(0, request.PasswordLength), remoteAddress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            ArrayPool<char>.Shared.Return(password);
        }
    }

    private async ValueTask<LoginAccountRequestReadResult?> ReadAccountRequestAsync(LoginConnectionSession session, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_configuration.PhaseTimeout);

        try
        {
            return await new LoginAccountRequestReader(session).ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return null;
        }
    }

    private async ValueTask ReadPostAuthenticationReportsAsync(LoginConnectionSession session, uint sessionUid, CancellationToken cancellationToken)
    {
        _ = await new LoginPostAuthenticationReportReader(session).ReadAsync(sessionUid, _configuration.PhaseTimeout, cancellationToken).ConfigureAwait(false);
    }
    private ValueTask SendFailureAsync(LoginConnectionSession session, LoginAccountAuthenticationFailureCode failureCode, CancellationToken cancellationToken)
    {
        return session.WriteAsync(LoginAccountAuthenticationResponsePacket.Failure(failureCode, _configuration.GameServerPort, _configuration.GameServerIp), cancellationToken);
    }
}
