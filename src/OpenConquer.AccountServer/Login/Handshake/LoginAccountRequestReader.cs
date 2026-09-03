using OpenConquer.AccountServer.Login.Connections;
using OpenConquer.Protocol.Login.Packets;

namespace OpenConquer.AccountServer.Login.Handshake;

/// <summary>
/// Consumes and decodes the account credential request from one established
/// login connection.
/// </summary>
internal sealed class LoginAccountRequestReader
{
    private readonly LoginConnectionSession _session;

    public LoginAccountRequestReader(LoginConnectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <summary>
    /// Reads the next login frame and classifies it as the native standard 5517
    /// account request, clean end-of-stream, an unexpected packet, or an
    /// invalid account request.
    /// </summary>
    /// <remarks>
    /// Transport failures and caller-requested cancellation propagate
    /// unchanged. The consumed inbound frame is disposed on every outcome.
    /// On success, ownership of the decoded request transfers through the
    /// returned result.
    /// </remarks>
    public async ValueTask<LoginAccountRequestReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        LoginInboundFrame? inboundFrame = await _session.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (inboundFrame is null)
        {
            return LoginAccountRequestReadResult.EndOfStream();
        }

        using LoginInboundFrame frame = inboundFrame;

        if (frame.PacketId != LoginAccountRequestPacket.PacketIdentifier)
        {
            return LoginAccountRequestReadResult.UnexpectedPacket(frame.PacketId);
        }

        LoginAccountRequest? request = null;

        try
        {
            if (!LoginAccountRequestPacket.TryDecodeStandard5517(frame.Payload.Span, _session.LoginSeed, out request))
            {
                return LoginAccountRequestReadResult.InvalidAccountRequest();
            }

            LoginAccountRequest decodedRequest = request ?? throw new InvalidOperationException("Successful account-request decoding did not return a request.");

            LoginAccountRequestReadResult result = LoginAccountRequestReadResult.Success(decodedRequest);

            request = null;

            return result;
        }
        finally
        {
            request?.Dispose();
        }
    }
}
