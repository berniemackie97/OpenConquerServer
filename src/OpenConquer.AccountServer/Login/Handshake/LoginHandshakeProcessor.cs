using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake.Authentication;
using OpenConquer.AccountServer.Login.Handshake.PostAuthentication;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin.Issuance;
using OpenConquer.Application.Realms;
using OpenConquer.Domain.Realms;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

internal sealed class LoginHandshakeProcessor(IAccountAuthenticator authenticator, GameLoginTicketIssuer ticketIssuer, IRealmRegistry realmRegistry, LoginHandshakeConfiguration configuration)
{
    private const ushort ProtectedAccountRequestPacketId = 1084;
    private const ushort MobileAccountRequestPacketId = 1098;

    private readonly IAccountAuthenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    private readonly GameLoginTicketIssuer _ticketIssuer = ticketIssuer ?? throw new ArgumentNullException(nameof(ticketIssuer));
    private readonly IRealmRegistry _realmRegistry = realmRegistry ?? throw new ArgumentNullException(nameof(realmRegistry));
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
        AccountAuthenticationResult authentication = default;
        RealmRegistration? selectedRealm;

        using (request)
        {
            selectedRealm = await FindRealmAsync(request.ServerName, cancellationToken).ConfigureAwait(false);
            if (selectedRealm is { Availability: RealmAvailability.Enabled })
            {
                authentication = await AuthenticateAsync(request, remoteAddress, cancellationToken).ConfigureAwait(false);
            }
        }

        if (selectedRealm is not { Availability: RealmAvailability.Enabled })
        {
            await SendFailureAsync(session, GetUnavailableRealmFailure(selectedRealm), cancellationToken).ConfigureAwait(false);
            return;
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

        // Authentication can take long enough for operators to change a realm's
        // availability or public endpoint. Recheck before granting a ticket.
        RealmRegistration? currentRealm = await FindRealmAsync(selectedRealm.ProtocolName.Value, cancellationToken).ConfigureAwait(false);

        if (currentRealm is null || currentRealm.Id != selectedRealm.Id)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.ServerDown, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (currentRealm.Availability != RealmAvailability.Enabled)
        {
            await SendFailureAsync(session, GetUnavailableRealmFailure(currentRealm), cancellationToken).ConfigureAwait(false);
            return;
        }

        GameLoginTicket? ticket = await _ticketIssuer.IssueAsync(authentication, currentRealm.Id, cancellationToken).ConfigureAwait(false);

        if (ticket is null)
        {
            await SendFailureAsync(session, LoginAccountAuthenticationFailureCode.InvalidCredentials, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginAccountAuthenticationResponsePacket response = LoginAccountAuthenticationResponsePacket.Success(ticket.SessionUid, ticket.AuthenticationKey, currentRealm.PublicGamePort, ticket.AuthenticationKey, currentRealm.PublicGameAddress.ToString());

        await session.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await ReadPostAuthenticationReportsAsync(session, ticket.SessionUid, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RealmRegistration?> FindRealmAsync(string serverName, CancellationToken cancellationToken)
    {
        ProtocolServerName protocolName;
        try
        {
            protocolName = new ProtocolServerName(serverName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        RealmRegistration? realm = await _realmRegistry.FindByNameAsync(protocolName, cancellationToken).ConfigureAwait(false);

        return realm is not null && realm.ProtocolName == protocolName ? realm : null;
    }

    // The client renders these as "Server Locked" and "The Server is down.". Unknown,
    // malformed and disabled selections share one reply, so retiring a realm by
    // disabling or by removing its registration looks the same to players.
    private static LoginAccountAuthenticationFailureCode GetUnavailableRealmFailure(RealmRegistration? realm)
    {
        return realm?.Availability == RealmAvailability.Maintenance
            ? LoginAccountAuthenticationFailureCode.ServerLocked
            : LoginAccountAuthenticationFailureCode.ServerDown;
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

    private static ValueTask SendFailureAsync(LoginConnectionSession session, LoginAccountAuthenticationFailureCode failureCode, CancellationToken cancellationToken)
    {
        return session.WriteAsync(LoginAccountAuthenticationResponsePacket.Failure(failureCode), cancellationToken);
    }
}
