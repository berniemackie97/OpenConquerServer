using System.Net;
using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Handshake.Authentication;
using OpenConquer.AccountServer.Login.Handshake.Registration;
using OpenConquer.AccountServer.Login.Handshake.Requests;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>Routes the first request on the seeded connection to its operation-specific handler.</summary>
internal sealed class LoginHandshakeProcessor(LoginAuthenticationHandler authentication, AccountRegistrationHandler registration,
    LoginHandshakeConfiguration configuration)
{
    private const ushort ProtectedAccountRequestPacketId = 1084;
    private const ushort MobileAccountRequestPacketId = 1098;

    private readonly LoginAuthenticationHandler _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    private readonly AccountRegistrationHandler _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    private readonly LoginHandshakeConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async ValueTask ProcessAsync(LoginConnectionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        IPAddress remoteAddress = session.RemoteEndPoint is IPEndPoint endpoint
            ? endpoint.Address
            : throw new InvalidOperationException("Account requests require an IP network endpoint.");

        using LoginRequestReadResult? result = await ReadRequestAsync(session, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return;
        }

        switch (result.Status)
        {
            case LoginRequestReadStatus.EndOfStream:
                return;
            case LoginRequestReadStatus.AuthenticationRequest:
                await _authentication.ProcessAsync(session, result.Request!, remoteAddress, cancellationToken).ConfigureAwait(false);
                return;
            case LoginRequestReadStatus.RegistrationRequest:
                await _registration.ProcessAsync(session, result.Registration!, remoteAddress, cancellationToken).ConfigureAwait(false);
                return;
            case LoginRequestReadStatus.InvalidRegistrationRequest:
                await session.WriteAsync(new LoginAccountRegistrationResponsePacket(LoginAccountRegistrationResultCode.InvalidRequest), cancellationToken).ConfigureAwait(false);
                return;
            case LoginRequestReadStatus.InvalidAccountRequest:
            case LoginRequestReadStatus.UnexpectedPacket:
                LoginAccountAuthenticationFailureCode failure = result.UnexpectedPacketId is ProtectedAccountRequestPacketId or MobileAccountRequestPacketId
                    ? LoginAccountAuthenticationFailureCode.InvalidCredentials
                    : LoginAccountAuthenticationFailureCode.InvalidAccount;
                await session.WriteAsync(LoginAccountAuthenticationResponsePacket.Failure(failure), cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException("The connection reader returned an unsupported request status.");
        }
    }

    private async ValueTask<LoginRequestReadResult?> ReadRequestAsync(LoginConnectionSession session, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_configuration.PhaseTimeout);
        try
        {
            return await new LoginRequestReader(session).ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return null;
        }
    }
}
