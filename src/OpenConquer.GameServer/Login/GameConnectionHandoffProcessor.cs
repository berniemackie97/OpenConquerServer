using OpenConquer.GameServer.Connections;
using OpenConquer.GameServer.Handshake;
using OpenConquer.Transport.Connections;

namespace OpenConquer.GameServer.Login;

/// <summary>
/// Takes ownership of an accepted transport connection and advances it through
/// secure transport establishment and game-login authentication.
/// </summary>
internal sealed class GameConnectionHandoffProcessor(GameTransportHandshakeProcessor handshakeProcessor, GameConnectionAuthenticator authenticator)
{
    private readonly GameTransportHandshakeProcessor _handshakeProcessor = handshakeProcessor ?? throw new ArgumentNullException(nameof(handshakeProcessor));
    private readonly GameConnectionAuthenticator _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

    public async ValueTask<GameConnectionAuthenticationResult> ProcessAsync(ITransportConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        GameConnectionSession? session = null;

        try
        {
            session = await GameConnectionSession.OpenAsync(connection, cancellationToken).ConfigureAwait(false);

            bool handshakeCompleted = await _handshakeProcessor.TryCompleteAsync(session, cancellationToken).ConfigureAwait(false);

            if (!handshakeCompleted)
            {
                GameConnectionSession closingSession = session;
                session = null;

                await closingSession.DisposeAsync().ConfigureAwait(false);

                return GameConnectionAuthenticationResult.PeerClosed();
            }

            GameConnectionSession authenticationSession = session;
            session = null;

            return await _authenticator.AuthenticateAsync(authenticationSession, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception processingException)
        {
            if (session is null)
            {
                throw;
            }

            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw CreateProcessingFailure(processingException, cleanupException);
            }

            throw;
        }
    }

    private static AggregateException CreateProcessingFailure(Exception processingException, Exception cleanupException)
    {
        List<Exception> failures = [processingException];

        if (cleanupException is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(cleanupException);
        }

        return new AggregateException("GameServer connection handoff failed and session cleanup also failed.", failures);
    }
}
