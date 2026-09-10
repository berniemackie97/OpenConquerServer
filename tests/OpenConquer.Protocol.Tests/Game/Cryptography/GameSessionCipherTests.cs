using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Tests.Game.Cryptography;

public sealed class GameSessionCipherTests
{
    [Fact]
    public void DirectionalStreams_MatchNativeServerIvAssignment()
    {
        byte[] key = Enumerable.Range(1, 64).Select(static value => (byte)value).ToArray();
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] outboundInitializationVector = Convert.FromHexString("1020304050607080");

        using GameSessionCipher session = new(key, inboundInitializationVector, outboundInitializationVector);

        byte[] clientPlaintext = "client-to-server"u8.ToArray();
        byte[] clientCiphertext = new byte[clientPlaintext.Length];
        byte[] serverPlaintext = new byte[clientPlaintext.Length];

        using (GameCast5Cfb64Cipher clientOutbound = new(key, inboundInitializationVector, encrypt: true))
        {
            clientOutbound.Transform(clientPlaintext, clientCiphertext);
        }

        session.DecryptInbound(clientCiphertext, serverPlaintext);

        Assert.Equal(clientPlaintext, serverPlaintext);

        byte[] serverOutboundPlaintext = "server-to-client"u8.ToArray();
        byte[] serverCiphertext = new byte[serverOutboundPlaintext.Length];
        byte[] clientDecrypted = new byte[serverOutboundPlaintext.Length];

        session.EncryptOutbound(serverOutboundPlaintext, serverCiphertext);

        using (GameCast5Cfb64Cipher clientInbound = new(key, outboundInitializationVector, encrypt: false))
        {
            clientInbound.Transform(serverCiphertext, clientDecrypted);
        }

        Assert.Equal(serverOutboundPlaintext, clientDecrypted);
    }

    [Fact]
    public void DirectionalStreams_PreserveStateAcrossChunks()
    {
        byte[] key = Enumerable.Range(1, 64).Select(static value => (byte)value).ToArray();
        byte[] inboundInitializationVector = Convert.FromHexString("0102030405060708");
        byte[] outboundInitializationVector = Convert.FromHexString("1020304050607080");
        byte[] plaintext = Enumerable.Range(0, 97).Select(static value => unchecked((byte)((value * 43) + 17))).ToArray();
        byte[] expected = new byte[plaintext.Length];
        byte[] actual = new byte[plaintext.Length];

        using (GameCast5Cfb64Cipher reference = new(key, outboundInitializationVector, encrypt: true))
        {
            reference.Transform(plaintext, expected);
        }

        using GameSessionCipher session = new(key, inboundInitializationVector, outboundInitializationVector);

        session.EncryptOutbound(plaintext.AsSpan(0, 3), actual.AsSpan(0, 3));
        session.EncryptOutbound(plaintext.AsSpan(3, 19), actual.AsSpan(3, 19));
        session.EncryptOutbound(plaintext.AsSpan(22, 1), actual.AsSpan(22, 1));
        session.EncryptOutbound(plaintext.AsSpan(23), actual.AsSpan(23));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DirectionalStreams_AreIndependent()
    {
        byte[] key = Enumerable.Range(1, 64).Select(static value => (byte)value).ToArray();
        byte[] initializationVector = Convert.FromHexString("0102030405060708");
        byte[] plaintext = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] decrypted = new byte[plaintext.Length];

        using GameSessionCipher session = new(key, initializationVector, initializationVector);

        session.EncryptOutbound(plaintext, ciphertext);
        session.DecryptInbound(ciphertext, decrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Dispose_TerminatesBothDirectionalStreams()
    {
        byte[] key = Enumerable.Range(1, 64).Select(static value => (byte)value).ToArray();
        byte[] initializationVector = new byte[GameCast5Cfb64Cipher.InitializationVectorLength];
        GameSessionCipher session = new(key, initializationVector, initializationVector);

        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.EncryptOutbound(new byte[1], new byte[1]));
        Assert.Throws<ObjectDisposedException>(() => session.DecryptInbound(new byte[1], new byte[1]));
    }
}
