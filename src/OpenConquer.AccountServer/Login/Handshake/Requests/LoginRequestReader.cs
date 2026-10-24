using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.AccountServer.Login.Connections.Framing;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake.Requests;

/// <summary>
/// Consumes and decodes the account credential request from one established login connection.
/// </summary>
internal sealed class LoginRequestReader
{
    private readonly LoginConnectionSession _session;

    public LoginRequestReader(LoginConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <summary>
    /// Reads the next login frame and classifies it as a standard account login request or an OpenConquer
    /// registration request.
    /// </summary>
    public async ValueTask<LoginRequestReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        LoginInboundFrame? inboundFrame = await _session.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (inboundFrame is null)
        {
            return LoginRequestReadResult.EndOfStream();
        }

        using LoginInboundFrame frame = inboundFrame;

        if (frame.PacketId == LoginAccountRegistrationRequestPacket.PacketIdentifier)
        {
            return LoginAccountRegistrationRequestPacket.TryDecode(frame.Payload.Span, _session.LoginSeed, out LoginAccountRegistrationRequest? registration)
                ? LoginRequestReadResult.RegistrationRequest(registration!)
                : LoginRequestReadResult.InvalidRegistrationRequest();
        }

        if (frame.PacketId != LoginAccountRequestPacket.PacketIdentifier)
        {
            return LoginRequestReadResult.UnexpectedPacket(frame.PacketId);
        }

        LoginAccountRequest? request = null;

        try
        {
            if (!LoginAccountRequestPacket.TryDecodeStandard(frame.Payload.Span, _session.LoginSeed, out request))
            {
                return LoginRequestReadResult.InvalidAccountRequest();
            }

            LoginAccountRequest decodedRequest = request ?? throw new InvalidOperationException("Successful account-request decoding did not return a request.");

            LoginRequestReadResult result = LoginRequestReadResult.AuthenticationRequest(decodedRequest);

            request = null;

            return result;
        }
        finally
        {
            request?.Dispose();
        }
    }
}
