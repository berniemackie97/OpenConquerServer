using System.Security.Cryptography;
using OpenConquer.Infrastructure.Security;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketAuthenticationKeyRingFactoryTests
{
    private const ushort ActiveKeyId = 7;
    private const ushort HistoricalKeyId = 3;

    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;

    private const string Base64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    [Fact]
    public void Create_WithCanonicalActiveKey_ProducesExpectedVerifier()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);

        try
        {
            string encodedActiveVerificationKey = Convert.ToBase64String(activeVerificationKey);

            byte[] expected = GameLoginTicketAuthenticationKeyVerifier.Create(
                activeVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            using GameLoginTicketAuthenticationKeyRing keyRing =
                GameLoginTicketAuthenticationKeyRingFactory.Create(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, string>(ActiveKeyId, encodedActiveVerificationKey)]
                );

            byte[] actual = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            Assert.Equal(ActiveKeyId, keyRing.ActiveKeyId);
            Assert.Equal(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
        }
    }

    [Fact]
    public void Create_WithHistoricalKey_AllowsRotationVerification()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            string encodedActiveVerificationKey = Convert.ToBase64String(activeVerificationKey);

            string encodedHistoricalVerificationKey = Convert.ToBase64String(
                historicalVerificationKey
            );

            byte[] historicalVerifier = GameLoginTicketAuthenticationKeyVerifier.Create(
                historicalVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            using GameLoginTicketAuthenticationKeyRing keyRing =
                GameLoginTicketAuthenticationKeyRingFactory.Create(
                    ActiveKeyId,
                    [
                        new KeyValuePair<ushort, string>(ActiveKeyId, encodedActiveVerificationKey),
                        new KeyValuePair<ushort, string>(
                            HistoricalKeyId,
                            encodedHistoricalVerificationKey
                        ),
                    ]
                );

            bool verified = keyRing.Verify(
                HistoricalKeyId,
                historicalVerifier,
                SessionUid,
                AuthenticationKey
            );

            Assert.True(verified);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Create_WhenEncodedVerificationKeysAreNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            CreateAndDispose(ActiveKeyId, null!)
        );

        Assert.Equal("encodedVerificationKeys", exception.ParamName);
    }

    [Fact]
    public void Create_WhenVerificationKeyIdIsZero_ThrowsArgumentException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            string encodedVerificationKey = Convert.ToBase64String(verificationKey);

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, string>(0, encodedVerificationKey)]
                )
            );

            Assert.Equal("encodedVerificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Create_WhenEncodedVerificationKeyIsNull_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateAndDispose(ActiveKeyId, [new KeyValuePair<ushort, string>(ActiveKeyId, null!)])
        );

        Assert.Equal("encodedVerificationKeys", exception.ParamName);
    }

    [Fact]
    public void Create_WhenEncodingHasCorrectLengthButInvalidBase64_ThrowsArgumentException()
    {
        string encodedVerificationKey = new('!', 44);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateAndDispose(
                ActiveKeyId,
                [new KeyValuePair<ushort, string>(ActiveKeyId, encodedVerificationKey)]
            )
        );

        Assert.Equal("encodedVerificationKeys", exception.ParamName);
    }

    [Fact]
    public void Create_WhenEncodingContainsWhitespace_ThrowsArgumentException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            string encodedVerificationKey = Convert.ToBase64String(verificationKey) + "\n";

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, string>(ActiveKeyId, encodedVerificationKey)]
                )
            );

            Assert.Equal("encodedVerificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Create_WhenDecodedVerificationKeyHasWrongLength_ThrowsArgumentException(
        int verificationKeyLength
    )
    {
        byte[] verificationKey = new byte[verificationKeyLength];

        try
        {
            string encodedVerificationKey = Convert.ToBase64String(verificationKey);

            Assert.Equal(44, encodedVerificationKey.Length);

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, string>(ActiveKeyId, encodedVerificationKey)]
                )
            );

            Assert.Equal("encodedVerificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Create_WhenBase64EncodingIsNotCanonical_ThrowsArgumentException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            string canonicalEncoding = Convert.ToBase64String(verificationKey);

            string nonCanonicalEncoding = CreateNonCanonicalEquivalentEncoding(canonicalEncoding);

            Assert.NotEqual(canonicalEncoding, nonCanonicalEncoding);

            byte[] canonicalDecoded = Convert.FromBase64String(canonicalEncoding);

            byte[] nonCanonicalDecoded = Convert.FromBase64String(nonCanonicalEncoding);

            try
            {
                Assert.Equal(canonicalDecoded, nonCanonicalDecoded);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(canonicalDecoded);
                CryptographicOperations.ZeroMemory(nonCanonicalDecoded);
            }

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, string>(ActiveKeyId, nonCanonicalEncoding)]
                )
            );

            Assert.Equal("encodedVerificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Create_WhenKeyIdIsDuplicated_ThrowsArgumentException()
    {
        byte[] firstVerificationKey = CreateVerificationKey(0x10);
        byte[] secondVerificationKey = CreateVerificationKey(0x40);

        try
        {
            string firstEncoding = Convert.ToBase64String(firstVerificationKey);

            string secondEncoding = Convert.ToBase64String(secondVerificationKey);

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [
                        new KeyValuePair<ushort, string>(ActiveKeyId, firstEncoding),
                        new KeyValuePair<ushort, string>(ActiveKeyId, secondEncoding),
                    ]
                )
            );

            Assert.Equal("encodedVerificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstVerificationKey);
            CryptographicOperations.ZeroMemory(secondVerificationKey);
        }
    }

    [Fact]
    public void Create_WhenActiveKeyIsNotConfigured_ThrowsArgumentException()
    {
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            string encodedHistoricalVerificationKey = Convert.ToBase64String(
                historicalVerificationKey
            );

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                CreateAndDispose(
                    ActiveKeyId,
                    [
                        new KeyValuePair<ushort, string>(
                            HistoricalKeyId,
                            encodedHistoricalVerificationKey
                        ),
                    ]
                )
            );

            Assert.Equal("activeKeyId", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Create_WhenActiveKeyIdIsZero_ThrowsArgumentOutOfRangeException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            string encodedVerificationKey = Convert.ToBase64String(verificationKey);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateAndDispose(
                    activeKeyId: 0,
                    [new KeyValuePair<ushort, string>(ActiveKeyId, encodedVerificationKey)]
                )
            );

            Assert.Equal("activeKeyId", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Create_WhenEncodingIsInvalid_DoesNotIncludeSecretMaterialInException()
    {
        string encodedVerificationKey = new('!', 44);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CreateAndDispose(
                ActiveKeyId,
                [new KeyValuePair<ushort, string>(ActiveKeyId, encodedVerificationKey)]
            )
        );

        Assert.DoesNotContain(encodedVerificationKey, exception.Message, StringComparison.Ordinal);
    }

    private static void CreateAndDispose(
        ushort activeKeyId,
        IEnumerable<KeyValuePair<ushort, string>> encodedVerificationKeys
    )
    {
        using GameLoginTicketAuthenticationKeyRing keyRing =
            GameLoginTicketAuthenticationKeyRingFactory.Create(
                activeKeyId,
                encodedVerificationKeys
            );
    }

    private static string CreateNonCanonicalEquivalentEncoding(string canonicalEncoding)
    {
        if (canonicalEncoding.Length != 44 || canonicalEncoding[^1] != '=')
        {
            throw new ArgumentException(
                "The supplied encoding is not a canonical 32-byte Base64 value.",
                nameof(canonicalEncoding)
            );
        }

        char[] characters = canonicalEncoding.ToCharArray();

        int finalDataCharacterIndex = characters.Length - 2;

        int canonicalValue = Base64Alphabet.IndexOf(
            characters[finalDataCharacterIndex],
            StringComparison.Ordinal
        );

        if (
            canonicalValue < 0
            || (canonicalValue & 0x03) != 0
            || canonicalValue == Base64Alphabet.Length - 1
        )
        {
            throw new InvalidOperationException(
                "The canonical Base64 value does not have the expected final quantum."
            );
        }

        characters[finalDataCharacterIndex] = Base64Alphabet[canonicalValue + 1];

        return new string(characters);
    }

    private static byte[] CreateVerificationKey(byte initialValue)
    {
        byte[] verificationKey = new byte[
            GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize
        ];

        for (int index = 0; index < verificationKey.Length; index++)
        {
            verificationKey[index] = unchecked((byte)(initialValue + index));
        }

        return verificationKey;
    }
}
