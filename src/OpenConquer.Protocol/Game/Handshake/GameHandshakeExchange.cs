using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using OpenConquer.Protocol.Game.Cryptography;

namespace OpenConquer.Protocol.Game.Handshake;

/// <summary>
/// Owns one server-side Diffie-Hellman exchange for the 5517 GameServer transport.
/// </summary>
public sealed class GameHandshakeExchange : IDisposable
{
    internal const string PrimeHex = "E7A69EBDF105F2A6BBDEAD7E798F76A209AD73FB466431E2E7352ED262F8C558F10BEFEA977DE9E21DCEE9B04D245F300ECCBBA03E72630556D011023F9E857F";
    internal const string GeneratorHex = "05";

    private const int ChallengePrefixLength = 11;
    private const int ChallengeJunkLength = 12;

    private static readonly BigInteger s_prime = ParseHex(PrimeHex);
    private static readonly BigInteger s_generator = ParseHex(GeneratorHex);
    private static readonly int s_sessionKeyMaterialLength = s_prime.GetByteCount(isUnsigned: true);

    private readonly object _lifecycleGate = new();
    private readonly byte[] _serverOutboundInitializationVector;
    private readonly byte[] _serverInboundInitializationVector;
    private readonly byte[] _encryptedChallenge;

    private byte[]? _privateExponent;
    private bool _completed;
    private bool _disposed;

    private GameHandshakeExchange(byte[] privateExponent, byte[] serverOutboundInitializationVector, byte[] serverInboundInitializationVector, byte[] encryptedChallenge)
    {
        _privateExponent = privateExponent;
        _serverOutboundInitializationVector = serverOutboundInitializationVector;
        _serverInboundInitializationVector = serverInboundInitializationVector;
        _encryptedChallenge = encryptedChallenge;
    }

    public ReadOnlyMemory<byte> EncryptedChallenge
    {
        get
        {
            lock (_lifecycleGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _encryptedChallenge;
            }
        }
    }

    public static GameHandshakeExchange Create()
    {
        byte[] privateExponent = GeneratePrivateExponent();
        byte[] outboundInitializationVector = RandomNumberGenerator.GetBytes(GameCast5Cfb64Cipher.InitializationVectorLength);
        byte[] inboundInitializationVector = RandomNumberGenerator.GetBytes(GameCast5Cfb64Cipher.InitializationVectorLength);
        byte[]? encryptedChallenge = null;

        try
        {
            BigInteger exponent = new(privateExponent, isUnsigned: true, isBigEndian: true);
            BigInteger publicKey = BigInteger.ModPow(s_generator, exponent, s_prime);
            string publicKeyHex = ToOpenSslHex(publicKey);

            byte[] plaintextChallenge = BuildChallenge(outboundInitializationVector, inboundInitializationVector, publicKeyHex);
            encryptedChallenge = new byte[plaintextChallenge.Length];

            try
            {
                GameHandshakeBootstrapCipher.Encrypt(plaintextChallenge, encryptedChallenge);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintextChallenge);
            }

            return new GameHandshakeExchange(privateExponent, outboundInitializationVector, inboundInitializationVector, encryptedChallenge);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(privateExponent);
            CryptographicOperations.ZeroMemory(outboundInitializationVector);
            CryptographicOperations.ZeroMemory(inboundInitializationVector);

            if (encryptedChallenge is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedChallenge);
            }

            throw;
        }
    }

    /// <summary>
    /// Completes this exchange exactly once and transfers the negotiated
    /// directional CAST state to a game session.
    /// </summary>
    public GameSessionCipher Complete(string clientPublicKeyHex)
    {
        ArgumentNullException.ThrowIfNull(clientPublicKeyHex);

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_completed)
            {
                throw new InvalidOperationException("The handshake exchange has already been completed.");
            }

            _completed = true;

            byte[] privateExponent = _privateExponent ?? throw new InvalidOperationException("The handshake private exponent is unavailable.");
            _privateExponent = null;

            byte[] sessionKeyMaterial = new byte[s_sessionKeyMaterialLength];

            try
            {
                if (!TryParseClientPublicKey(clientPublicKeyHex, out BigInteger clientPublicKey))
                {
                    throw new InvalidDataException("The client Diffie-Hellman public key is invalid.");
                }

                BigInteger exponent = new(privateExponent, isUnsigned: true, isBigEndian: true);
                BigInteger sharedSecret = BigInteger.ModPow(clientPublicKey, exponent, s_prime);
                byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);

                try
                {
                    if (sharedSecretBytes.Length > sessionKeyMaterial.Length)
                    {
                        throw new CryptographicException("The Diffie-Hellman shared secret exceeded the negotiated group size.");
                    }

                    // Native OpenSSL DH_compute_key writes the minimal big-endian
                    // secret at the beginning of a zeroed DH_size-sized buffer.
                    sharedSecretBytes.CopyTo(sessionKeyMaterial, 0);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(sharedSecretBytes);
                }

                return new GameSessionCipher(sessionKeyMaterial, _serverInboundInitializationVector, _serverOutboundInitializationVector);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateExponent);
                CryptographicOperations.ZeroMemory(sessionKeyMaterial);
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_privateExponent is not null)
            {
                CryptographicOperations.ZeroMemory(_privateExponent);
                _privateExponent = null;
            }

            CryptographicOperations.ZeroMemory(_serverOutboundInitializationVector);
            CryptographicOperations.ZeroMemory(_serverInboundInitializationVector);
            CryptographicOperations.ZeroMemory(_encryptedChallenge);
        }
    }

    private static byte[] BuildChallenge(ReadOnlySpan<byte> outboundInitializationVector, ReadOnlySpan<byte> inboundInitializationVector, string publicKeyHex)
    {
        byte[] junk = RandomNumberGenerator.GetBytes(ChallengeJunkLength);

        try
        {
            byte[] prime = Encoding.ASCII.GetBytes(PrimeHex);
            byte[] generator = Encoding.ASCII.GetBytes(GeneratorHex);
            byte[] publicKey = Encoding.ASCII.GetBytes(publicKeyHex);

            int declaredLength = checked(sizeof(int) + GetChunkWireLength(junk.Length) + GetChunkWireLength(outboundInitializationVector.Length) +
                                         GetChunkWireLength(inboundInitializationVector.Length) + GetChunkWireLength(prime.Length) +
                                         GetChunkWireLength(generator.Length) + GetChunkWireLength(publicKey.Length) + GameWireProtocol.SignatureLength);

            byte[] challenge = new byte[checked(ChallengePrefixLength + declaredLength)];
            RandomNumberGenerator.Fill(challenge.AsSpan(0, ChallengePrefixLength));

            int offset = ChallengePrefixLength;
            BinaryPrimitives.WriteInt32LittleEndian(challenge.AsSpan(offset), declaredLength);
            offset += sizeof(int);

            WriteChunk(challenge, ref offset, junk);
            WriteChunk(challenge, ref offset, outboundInitializationVector);
            WriteChunk(challenge, ref offset, inboundInitializationVector);
            WriteChunk(challenge, ref offset, prime);
            WriteChunk(challenge, ref offset, generator);
            WriteChunk(challenge, ref offset, publicKey);
            GameWireProtocol.ServerSignature.CopyTo(challenge.AsSpan(offset));

            return challenge;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(junk);
        }
    }

    private static byte[] GeneratePrivateExponent()
    {
        byte[] candidate = new byte[s_sessionKeyMaterialLength];
        BigInteger upperBound = s_prime - BigInteger.One;

        while (true)
        {
            RandomNumberGenerator.Fill(candidate);
            BigInteger value = new(candidate, isUnsigned: true, isBigEndian: true);

            if (value > BigInteger.One && value < upperBound)
            {
                return candidate;
            }
        }
    }

    private static bool TryParseClientPublicKey(ReadOnlySpan<char> encoded, out BigInteger publicKey)
    {
        publicKey = BigInteger.Zero;

        if (encoded.IsEmpty || encoded.Length > PrimeHex.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[(encoded.Length + 1) / 2];
        int sourceIndex = 0;
        int destinationIndex = 0;

        if ((encoded.Length & 1) != 0)
        {
            int nibble = ParseHexNibble(encoded[0]);

            if (nibble < 0)
            {
                return false;
            }

            bytes[0] = (byte)nibble;
            sourceIndex = 1;
            destinationIndex = 1;
        }

        while (sourceIndex < encoded.Length)
        {
            int highNibble = ParseHexNibble(encoded[sourceIndex]);
            int lowNibble = ParseHexNibble(encoded[sourceIndex + 1]);

            if (highNibble < 0 || lowNibble < 0)
            {
                return false;
            }

            bytes[destinationIndex++] = (byte)((highNibble << 4) | lowNibble);
            sourceIndex += 2;
        }

        publicKey = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return publicKey > BigInteger.One && publicKey < s_prime - BigInteger.One;
    }

    private static int ParseHexNibble(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return value - '0';
        }

        if (value is >= 'A' and <= 'F')
        {
            return value - 'A' + 10;
        }

        if (value is >= 'a' and <= 'f')
        {
            return value - 'a' + 10;
        }

        return -1;
    }

    private static BigInteger ParseHex(string value) => new(Convert.FromHexString(value), isUnsigned: true, isBigEndian: true);

    private static string ToOpenSslHex(BigInteger value)
    {
        string hex = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static int GetChunkWireLength(int length) => checked(sizeof(int) + length);

    private static void WriteChunk(Span<byte> destination, ref int offset, ReadOnlySpan<byte> value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], value.Length);
        offset += sizeof(int);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }
}
