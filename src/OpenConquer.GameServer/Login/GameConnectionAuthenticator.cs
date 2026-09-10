using System.Net;
using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Game.Packets;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Authenticates the first secured GameServer frame by validating the native
/// 1052 login proof and atomically redeeming its single-use login ticket.
/// </summary>
internal sealed class GameConnectionAuthenticator(GameLoginTicketRedeemer ticketRedeemer)
{
    private readonly GameLoginTicketRedeemer _ticketRedeemer = ticketRedeemer ?? throw new ArgumentNullException(nameof(ticketRedeemer));

    /// <summary>
    /// Takes ownership of <paramref name="session"/> and returns an authenticated
    /// connection only when its first secured frame proves a valid login ticket.
    /// </summary>
    public async ValueTask<GameConnectionAuthenticationResult> AuthenticateAsync(GameConnectionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        GameConnectionAuthenticationResult result;

        try
        {
            result = await AuthenticateCoreAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception authenticationException)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw CreateAuthenticationFailure(authenticationException, cleanupException);
            }

            throw;
        }

        if (result.Status != GameConnectionAuthenticationStatus.Authenticated)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        return result;
    }

    private async ValueTask<GameConnectionAuthenticationResult> AuthenticateCoreAsync(GameConnectionSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IPAddress remoteAddress = session.RemoteEndPoint is IPEndPoint endpoint
            ? endpoint.Address
            : throw new InvalidOperationException("GameServer authentication requires an IP network endpoint.");

        GameLoginProof1052 proof;

        using (GameInboundFrame? frame = await session.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (frame is null)
            {
                return GameConnectionAuthenticationResult.PeerClosed();
            }

            if (!GameLoginProof1052.TryParse(frame, out proof, out GameLoginProofParseError parseError))
            {
                throw new InvalidDataException($"The first secured GameServer frame is not a valid login proof 1052: {parseError}.");
            }
        }

        GameLoginTicketIdentity? identity = await _ticketRedeemer.RedeemAsync(proof.SessionUid, proof.AuthenticationKey, remoteAddress, cancellationToken).ConfigureAwait(false);

        if (identity is null)
        {
            return GameConnectionAuthenticationResult.AuthorizationRejected();
        }

        AuthenticatedGameConnection authenticatedConnection = new(identity.AccountId, identity.Username, identity.SessionUid, proof.LocaleTag, proof.HardwareAddress, proof.ResourceVersion, session);

        return GameConnectionAuthenticationResult.Authenticated(authenticatedConnection);
    }

    private static AggregateException CreateAuthenticationFailure(Exception authenticationException, Exception cleanupException)
    {
        List<Exception> failures = [authenticationException];

        if (cleanupException is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupException);
        }

        return new AggregateException("GameServer connection authentication failed and session cleanup also failed.", failures);
    }
}
