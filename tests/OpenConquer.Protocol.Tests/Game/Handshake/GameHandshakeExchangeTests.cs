using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OpenConquer.Protocol.Game;
using OpenConquer.Protocol.Game.Cryptography;
using OpenConquer.Protocol.Game.Handshake;

namespace OpenConquer.Protocol.Tests.Game.Handshake;

public sealed class GameHandshakeExchangeTests
{
    [Fact]
    public void Create_EmitsOpenConquerPublicCompatibleServerChallenge()
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        DecodedChallenge challenge = DecodeChallenge(exchange.EncryptedChallenge.Span);

        Assert.Equal(12, challenge.Junk.Length);
        Assert.Equal(GameCast5Cfb64Cipher.InitializationVectorLength, challenge.ServerOutboundInitializationVector.Length);
        Assert.Equal(GameCast5Cfb64Cipher.InitializationVectorLength, challenge.ServerInboundInitializationVector.Length);
        Assert.Equal(GameHandshakeExchange.PrimeHex, challenge.PrimeHex);
        Assert.Equal(GameHandshakeExchange.GeneratorHex, challenge.GeneratorHex);

        BigInteger prime = ParseHex(challenge.PrimeHex);
        BigInteger serverPublicKey = ParseHex(challenge.ServerPublicKeyHex);

        Assert.True(serverPublicKey > BigInteger.One);
        Assert.True(serverPublicKey < prime - BigInteger.One);
    }

    [Fact]
    public void Complete_DerivesNativeOpenSslSharedSecretLayoutAndDirectionalState()
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        DecodedChallenge challenge = DecodeChallenge(exchange.EncryptedChallenge.Span);

        BigInteger prime = ParseHex(challenge.PrimeHex);
        BigInteger generator = ParseHex(challenge.GeneratorHex);
        BigInteger serverPublicKey = ParseHex(challenge.ServerPublicKeyHex);
        BigInteger clientPrivateExponent = new(2);
        BigInteger clientPublicKey = BigInteger.ModPow(generator, clientPrivateExponent, prime);
        BigInteger sharedSecret = BigInteger.ModPow(serverPublicKey, clientPrivateExponent, prime);

        byte[] sessionKeyMaterial = new byte[prime.GetByteCount(isUnsigned: true)];
        byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);

        try
        {
            sharedSecretBytes.CopyTo(sessionKeyMaterial, 0);

            using GameSessionCipher sessionCipher = exchange.Complete(ToOpenSslHex(clientPublicKey));

            byte[] inboundPlaintext = Enumerable.Range(0, 73).Select(static value => unchecked((byte)((value * 37) + 11))).ToArray();
            byte[] inboundCiphertext = new byte[inboundPlaintext.Length];
            byte[] inboundDecrypted = new byte[inboundPlaintext.Length];

            using (GameCast5Cfb64Cipher clientOutbound = new(sessionKeyMaterial, challenge.ServerInboundInitializationVector, encrypt: true))
            {
                clientOutbound.Transform(inboundPlaintext, inboundCiphertext);
            }

            sessionCipher.DecryptInbound(inboundCiphertext, inboundDecrypted);

            Assert.Equal(inboundPlaintext, inboundDecrypted);

            byte[] outboundPlaintext = Enumerable.Range(0, 79).Select(static value => unchecked((byte)((value * 41) + 13))).ToArray();
            byte[] outboundCiphertext = new byte[outboundPlaintext.Length];
            byte[] outboundDecrypted = new byte[outboundPlaintext.Length];

            sessionCipher.EncryptOutbound(outboundPlaintext, outboundCiphertext);

            using (GameCast5Cfb64Cipher clientInbound = new(sessionKeyMaterial, challenge.ServerOutboundInitializationVector, encrypt: false))
            {
                clientInbound.Transform(outboundCiphertext, outboundDecrypted);
            }

            Assert.Equal(outboundPlaintext, outboundDecrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecretBytes);
            CryptographicOperations.ZeroMemory(sessionKeyMaterial);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("GG")]
    public void Complete_RejectsInvalidClientPublicKeyAndConsumesExchange(string clientPublicKeyHex)
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        Assert.Throws<InvalidDataException>(() => exchange.Complete(clientPublicKeyHex));
        Assert.Throws<InvalidOperationException>(() => exchange.Complete("05"));
    }

    [Fact]
    public void Complete_RejectsPublicKeyOutsideNegotiatedGroup()
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        BigInteger prime = ParseHex(GameHandshakeExchange.PrimeHex);

        Assert.Throws<InvalidDataException>(() => exchange.Complete(ToOpenSslHex(prime - BigInteger.One)));
    }

    [Fact]
    public void Complete_CanOnlyTransferSessionStateOnce()
    {
        using GameHandshakeExchange exchange = GameHandshakeExchange.Create();
        DecodedChallenge challenge = DecodeChallenge(exchange.EncryptedChallenge.Span);

        BigInteger prime = ParseHex(challenge.PrimeHex);
        BigInteger generator = ParseHex(challenge.GeneratorHex);
        string clientPublicKeyHex = ToOpenSslHex(BigInteger.ModPow(generator, new BigInteger(2), prime));

        using GameSessionCipher sessionCipher = exchange.Complete(clientPublicKeyHex);

        Assert.Throws<InvalidOperationException>(() => exchange.Complete(clientPublicKeyHex));
    }

    [Fact]
    public void Dispose_RejectsChallengeAccessAndCompletion()
    {
        GameHandshakeExchange exchange = GameHandshakeExchange.Create();

        exchange.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = exchange.EncryptedChallenge);
        Assert.Throws<ObjectDisposedException>(() => exchange.Complete("05"));
    }

    private static DecodedChallenge DecodeChallenge(ReadOnlySpan<byte> encryptedChallenge)
    {
        byte[] plaintext = new byte[encryptedChallenge.Length];
        GameHandshakeBootstrapCipher.Decrypt(encryptedChallenge, plaintext);

        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(11));

        Assert.Equal(plaintext.Length, 11 + declaredLength);
        Assert.True(plaintext.AsSpan(plaintext.Length - GameWireProtocol.SignatureLength).SequenceEqual(GameWireProtocol.ServerSignature));

        int offset = 11 + sizeof(int);
        byte[] junk = ReadChunk(plaintext, ref offset);
        byte[] serverOutboundInitializationVector = ReadChunk(plaintext, ref offset);
        byte[] serverInboundInitializationVector = ReadChunk(plaintext, ref offset);
        string primeHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));
        string generatorHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));
        string serverPublicKeyHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));

        Assert.Equal(plaintext.Length - GameWireProtocol.SignatureLength, offset);

        return new DecodedChallenge(junk, serverOutboundInitializationVector, serverInboundInitializationVector, primeHex, generatorHex, serverPublicKeyHex);
    }

    private static byte[] ReadChunk(ReadOnlySpan<byte> source, ref int offset)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);

        byte[] value = source.Slice(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static BigInteger ParseHex(string value) =>
        new(Convert.FromHexString((value.Length & 1) == 0 ? value : string.Concat("0", value)), isUnsigned: true, isBigEndian: true);

    private static string ToOpenSslHex(BigInteger value)
    {
        string hex = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private sealed record DecodedChallenge(byte[] Junk, byte[] ServerOutboundInitializationVector, byte[] ServerInboundInitializationVector, string PrimeHex, string GeneratorHex, string ServerPublicKeyHex);
}
