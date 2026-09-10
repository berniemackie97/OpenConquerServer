using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OpenConquer.Protocol.Game;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace OpenConquer.GameServer.Tests.Connections;

internal sealed class GameClientTestPeer : IDisposable
{
    private const int ChallengePrefixLength = 11;
    private const int ResponsePrefixLength = 7;
    private const int InitializationVectorLength = 8;

    private static ReadOnlySpan<byte> BootstrapKey => "BC234xs45nme7HU9"u8;

    private readonly Cast5Cfb64Cipher _outbound;
    private readonly Cast5Cfb64Cipher _inbound;

    private GameClientTestPeer(string publicKeyHex, byte[] encryptedKeyExchangeResponse, Cast5Cfb64Cipher outbound, Cast5Cfb64Cipher inbound)
    {
        PublicKeyHex = publicKeyHex;
        EncryptedKeyExchangeResponse = encryptedKeyExchangeResponse;
        _outbound = outbound;
        _inbound = inbound;
    }

    public string PublicKeyHex { get; }
    public byte[] EncryptedKeyExchangeResponse { get; }

    public static GameClientTestPeer Create(ReadOnlySpan<byte> encryptedChallenge)
    {
        byte[] plaintextChallenge = TransformBootstrap(encryptedChallenge, encrypt: false);

        try
        {
            Challenge challenge = ParseChallenge(plaintextChallenge);
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

                string publicKeyHex = ToOpenSslHex(clientPublicKey);
                byte[] response = BuildEncryptedKeyExchangeResponse(publicKeyHex);
                Cast5Cfb64Cipher outbound = new(sessionKeyMaterial, challenge.ServerInboundInitializationVector, encrypt: true);

                try
                {
                    Cast5Cfb64Cipher inbound = new(sessionKeyMaterial, challenge.ServerOutboundInitializationVector, encrypt: false);
                    return new GameClientTestPeer(publicKeyHex, response, outbound, inbound);
                }
                catch
                {
                    outbound.Dispose();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sharedSecretBytes);
                CryptographicOperations.ZeroMemory(sessionKeyMaterial);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextChallenge);
        }
    }

    public byte[] EncryptClientFrame(ReadOnlySpan<byte> packet)
    {
        byte[] plaintext = new byte[packet.Length + GameWireProtocol.SignatureLength];
        packet.CopyTo(plaintext);
        GameWireProtocol.ClientSignature.CopyTo(plaintext.AsSpan(packet.Length));

        byte[] encrypted = new byte[plaintext.Length];

        try
        {
            _outbound.Transform(plaintext, encrypted);
            return encrypted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] DecryptServerBytes(ReadOnlySpan<byte> encrypted)
    {
        byte[] plaintext = new byte[encrypted.Length];
        _inbound.Transform(encrypted, plaintext);
        return plaintext;
    }

    public void Dispose()
    {
        _outbound.Dispose();
        _inbound.Dispose();
        CryptographicOperations.ZeroMemory(EncryptedKeyExchangeResponse);
    }

    private static Challenge ParseChallenge(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length < ChallengePrefixLength + sizeof(int) + GameWireProtocol.SignatureLength)
        {
            throw new InvalidDataException("Server challenge is too short.");
        }

        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(plaintext[ChallengePrefixLength..]);

        if (declaredLength <= 0 || plaintext.Length != ChallengePrefixLength + declaredLength)
        {
            throw new InvalidDataException("Server challenge contains an invalid declared length.");
        }

        int offset = ChallengePrefixLength + sizeof(int);
        _ = ReadChunk(plaintext, ref offset);
        byte[] serverOutboundInitializationVector = ReadChunk(plaintext, ref offset);
        byte[] serverInboundInitializationVector = ReadChunk(plaintext, ref offset);
        string primeHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));
        string generatorHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));
        string serverPublicKeyHex = Encoding.ASCII.GetString(ReadChunk(plaintext, ref offset));

        if (serverOutboundInitializationVector.Length != InitializationVectorLength || serverInboundInitializationVector.Length != InitializationVectorLength)
        {
            throw new InvalidDataException("Server challenge contains an invalid CAST initialization vector.");
        }

        if (offset != plaintext.Length - GameWireProtocol.SignatureLength || !plaintext[offset..].SequenceEqual(GameWireProtocol.ServerSignature))
        {
            throw new InvalidDataException("Server challenge contains an invalid signature or payload layout.");
        }

        return new Challenge(serverOutboundInitializationVector, serverInboundInitializationVector, primeHex, generatorHex, serverPublicKeyHex);
    }

    private static byte[] BuildEncryptedKeyExchangeResponse(string publicKeyHex)
    {
        byte[] publicKey = Encoding.ASCII.GetBytes(publicKeyHex);
        byte[] randomPadding = Enumerable.Range(1, 12).Select(static value => checked((byte)value)).ToArray();
        int declaredLength = checked(sizeof(int) + sizeof(int) + randomPadding.Length + sizeof(int) + publicKey.Length + GameWireProtocol.SignatureLength);
        byte[] plaintext = new byte[ResponsePrefixLength + declaredLength];

        for (int index = 0; index < ResponsePrefixLength; index++)
        {
            plaintext[index] = (byte)(0xA0 + index);
        }

        int offset = ResponsePrefixLength;
        BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset), declaredLength);
        offset += sizeof(int);

        WriteChunk(plaintext, ref offset, randomPadding);
        WriteChunk(plaintext, ref offset, publicKey);
        GameWireProtocol.ClientSignature.CopyTo(plaintext.AsSpan(offset));

        try
        {
            return TransformBootstrap(plaintext, encrypt: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(randomPadding);
        }
    }

    private static byte[] TransformBootstrap(ReadOnlySpan<byte> input, bool encrypt)
    {
        byte[] output = new byte[input.Length];

        using Cast5Cfb64Cipher cipher = new(BootstrapKey, new byte[InitializationVectorLength], encrypt);
        cipher.Transform(input, output);

        return output;
    }

    private static byte[] ReadChunk(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset > source.Length - sizeof(int))
        {
            throw new InvalidDataException("Handshake chunk length is truncated.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);

        if (length < 0 || length > source.Length - offset)
        {
            throw new InvalidDataException("Handshake chunk exceeds the containing payload.");
        }

        byte[] value = source.Slice(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static void WriteChunk(Span<byte> destination, ref int offset, ReadOnlySpan<byte> value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], value.Length);
        offset += sizeof(int);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static BigInteger ParseHex(string value) =>
        new(Convert.FromHexString((value.Length & 1) == 0 ? value : string.Concat("0", value)), isUnsigned: true, isBigEndian: true);

    private static string ToOpenSslHex(BigInteger value)
    {
        string hex = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private sealed record Challenge(byte[] ServerOutboundInitializationVector, byte[] ServerInboundInitializationVector, string PrimeHex,
        string GeneratorHex, string ServerPublicKeyHex);

    private sealed class Cast5Cfb64Cipher : IDisposable
    {
        private const int MaximumKeyLength = 16;
        private const int FeedbackMask = InitializationVectorLength - 1;

        private readonly Cast5Engine _engine = new();
        private readonly byte[] _feedbackRegister = new byte[InitializationVectorLength];
        private readonly byte[] _keystreamBlock = new byte[InitializationVectorLength];
        private readonly bool _encrypt;

        private int _feedbackPosition;
        private int _disposed;

        public Cast5Cfb64Cipher(ReadOnlySpan<byte> key, ReadOnlySpan<byte> initializationVector, bool encrypt)
        {
            if (key.IsEmpty)
            {
                throw new ArgumentException("Key material must not be empty.", nameof(key));
            }

            if (initializationVector.Length != InitializationVectorLength)
            {
                throw new ArgumentException($"Initialization vector must contain exactly {InitializationVectorLength} bytes.", nameof(initializationVector));
            }

            byte[] normalizedKey = key[..Math.Min(key.Length, MaximumKeyLength)].ToArray();

            try
            {
                _engine.Init(true, new KeyParameter(normalizedKey));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(normalizedKey);
            }

            initializationVector.CopyTo(_feedbackRegister);
            _encrypt = encrypt;
        }

        public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (output.Length < input.Length)
            {
                throw new ArgumentException("Output must be at least as long as input.", nameof(output));
            }

            for (int index = 0; index < input.Length; index++)
            {
                if (_feedbackPosition == 0)
                {
                    _engine.ProcessBlock(_feedbackRegister, 0, _keystreamBlock, 0);
                }

                byte inputByte = input[index];

                if (_encrypt)
                {
                    byte encryptedByte = (byte)(inputByte ^ _keystreamBlock[_feedbackPosition]);
                    output[index] = encryptedByte;
                    _feedbackRegister[_feedbackPosition] = encryptedByte;
                }
                else
                {
                    output[index] = (byte)(inputByte ^ _keystreamBlock[_feedbackPosition]);
                    _feedbackRegister[_feedbackPosition] = inputByte;
                }

                _feedbackPosition = (_feedbackPosition + 1) & FeedbackMask;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(_feedbackRegister);
            CryptographicOperations.ZeroMemory(_keystreamBlock);
            _feedbackPosition = 0;
        }
    }
}
