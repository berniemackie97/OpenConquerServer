using OpenConquer.GameServer.Connections;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;

namespace OpenConquer.GameServer.Tests.Connections;

internal static class GameConnectionTestHandshake
{
    public static async Task<GameClientTestPeer> CompleteAsync(GameConnectionSession session,
        FakeGameTransportConnection transport, CancellationToken cancellationToken)
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        GameClientTestPeer client = GameClientTestPeer.Create(exchange.EncryptedChallenge.Span);

        try
        {
            await session.SendHandshakeChallengeAsync(exchange.EncryptedChallenge, cancellationToken);
            transport.QueueReceive(client.EncryptedKeyExchangeResponse);

            string publicKey = Assert.IsType<string>(
                await session.ReadClientKeyExchangeResponseAsync(cancellationToken));
            Assert.Equal(client.PublicKeyHex, publicKey);

            GameSessionCipher? cipher = exchange.Complete(publicKey);
            try
            {
                session.CompleteHandshake(cipher);
                cipher = null;
            }
            finally
            {
                cipher?.Dispose();
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
