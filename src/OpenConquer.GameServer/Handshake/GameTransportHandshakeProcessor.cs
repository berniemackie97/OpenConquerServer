using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;

namespace OpenConquer.GameServer.Handshake;

/// <summary>
/// Performs the native 5517 Diffie-Hellman transport handshake and transfers
/// the negotiated secured transport state into a live GameServer session.
/// </summary>
internal sealed class GameTransportHandshakeProcessor
{
    public async ValueTask<bool> TryCompleteAsync(GameConnectionSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, cancellationToken).ConfigureAwait(false);

        string? clientPublicKeyHex = await session.ReadClientKeyExchangeResponseAsync(cancellationToken).ConfigureAwait(false);

        if (clientPublicKeyHex is null)
        {
            return false;
        }

        GameSessionCipher? sessionCipher = null;

        try
        {
            sessionCipher = exchange.Complete(clientPublicKeyHex);

            session.CompleteHandshake(sessionCipher);

            sessionCipher = null;

            return true;
        }
        finally
        {
            sessionCipher?.Dispose();
        }
    }
}
