using System.Security.Cryptography;
using OpenConquer.Infrastructure.Security;
using OpenConquer.Infrastructure.Security.Accounts.GameLogin;

namespace OpenConquer.Infrastructure.Tests.Security;

public sealed class GameLoginTicketAuthenticationKeyRingTests
{
    private const ushort ActiveKeyId = 7;
    private const ushort HistoricalKeyId = 3;

    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_WhenActiveKeyIdIsZero_ThrowsArgumentOutOfRangeException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                ConstructAndDispose(
                    activeKeyId: 0,
                    [new KeyValuePair<ushort, byte[]>(ActiveKeyId, verificationKey)]
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
    public void Constructor_WhenVerificationKeysAreNull_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            ConstructAndDispose(ActiveKeyId, null!)
        );

        Assert.Equal("verificationKeys", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenVerificationKeyIdIsZero_ThrowsArgumentException()
    {
        byte[] verificationKey = CreateVerificationKey(0x10);

        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                ConstructAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, byte[]>(0, verificationKey)]
                )
            );

            Assert.Equal("verificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Constructor_WhenVerificationKeyMaterialIsNull_ThrowsArgumentException()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ConstructAndDispose(ActiveKeyId, [new KeyValuePair<ushort, byte[]>(ActiveKeyId, null!)])
        );

        Assert.Equal("verificationKeys", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_WhenVerificationKeyHasWrongLength_ThrowsArgumentException(
        int verificationKeyLength
    )
    {
        byte[] verificationKey = new byte[verificationKeyLength];

        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                ConstructAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, byte[]>(ActiveKeyId, verificationKey)]
                )
            );

            Assert.Equal("verificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    [Fact]
    public void Constructor_WhenVerificationKeyIdIsDuplicated_ThrowsArgumentException()
    {
        byte[] firstVerificationKey = CreateVerificationKey(0x10);
        byte[] secondVerificationKey = CreateVerificationKey(0x40);

        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                ConstructAndDispose(
                    ActiveKeyId,
                    [
                        new KeyValuePair<ushort, byte[]>(ActiveKeyId, firstVerificationKey),
                        new KeyValuePair<ushort, byte[]>(ActiveKeyId, secondVerificationKey),
                    ]
                )
            );

            Assert.Equal("verificationKeys", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstVerificationKey);
            CryptographicOperations.ZeroMemory(secondVerificationKey);
        }
    }

    [Fact]
    public void Constructor_WhenActiveKeyIsNotConfigured_ThrowsArgumentException()
    {
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                ConstructAndDispose(
                    ActiveKeyId,
                    [new KeyValuePair<ushort, byte[]>(HistoricalKeyId, historicalVerificationKey)]
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
    public void CreateVerifier_UsesConfiguredActiveKey()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            byte[] expected = GameLoginTicketAuthenticationKeyVerifier.Create(
                activeVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] actual = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            Assert.Equal(ActiveKeyId, keyRing.ActiveKeyId);
            Assert.Equal(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Verify_AcceptsVerifierCreatedByActiveKey()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            bool verified = keyRing.Verify(ActiveKeyId, verifier, SessionUid, AuthenticationKey);

            Assert.True(verified);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Verify_AcceptsVerifierCreatedByHistoricalKey()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            byte[] verifier = GameLoginTicketAuthenticationKeyVerifier.Create(
                historicalVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            bool verified = keyRing.Verify(
                HistoricalKeyId,
                verifier,
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
    public void Verify_WhenAuthenticationKeyDoesNotMatch_ReturnsFalse()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            bool verified = keyRing.Verify(
                ActiveKeyId,
                verifier,
                SessionUid,
                AuthenticationKey + 1
            );

            Assert.False(verified);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Verify_WhenDifferentConfiguredKeyIdIsSelected_ReturnsFalse()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            bool verified = keyRing.Verify(
                HistoricalKeyId,
                verifier,
                SessionUid,
                AuthenticationKey
            );

            Assert.False(verified);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Constructor_ClonesCallerOwnedVerificationKeyMaterial()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            byte[] expected = GameLoginTicketAuthenticationKeyVerifier.Create(
                activeVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);

            byte[] actual = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            Assert.Equal(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Verify_WhenVerificationKeyIdIsZero_ThrowsArgumentOutOfRangeException()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                keyRing.Verify(verificationKeyId: 0, verifier, SessionUid, AuthenticationKey)
            );

            Assert.Equal("verificationKeyId", exception.ParamName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Verify_WhenVerificationKeyIdIsUnknown_ThrowsKeyNotFoundException()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            Assert.Throws<KeyNotFoundException>(() =>
                keyRing.Verify(verificationKeyId: 99, verifier, SessionUid, AuthenticationKey)
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public void Dispose_IsIdempotentAndRejectsSubsequentCryptographicOperations()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] verifier = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

            keyRing.Dispose();
            keyRing.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                keyRing.CreateVerifier(SessionUid, AuthenticationKey)
            );

            Assert.Throws<ObjectDisposedException>(() =>
                keyRing.Verify(ActiveKeyId, verifier, SessionUid, AuthenticationKey)
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    [Fact]
    public async Task ConcurrentCreateAndVerifyOperations_RemainConsistent()
    {
        byte[] activeVerificationKey = CreateVerificationKey(0x10);
        byte[] historicalVerificationKey = CreateVerificationKey(0x40);

        try
        {
            using GameLoginTicketAuthenticationKeyRing keyRing = CreateKeyRing(
                activeVerificationKey,
                historicalVerificationKey
            );

            byte[] expectedActiveVerifier = GameLoginTicketAuthenticationKeyVerifier.Create(
                activeVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            byte[] expectedHistoricalVerifier = GameLoginTicketAuthenticationKeyVerifier.Create(
                historicalVerificationKey,
                SessionUid,
                AuthenticationKey
            );

            Task[] operations = new Task[64];

            for (int index = 0; index < operations.Length; index++)
            {
                int operation = index;

                operations[index] = Task.Run(
                    () =>
                    {
                        if ((operation & 1) == 0)
                        {
                            byte[] actual = keyRing.CreateVerifier(SessionUid, AuthenticationKey);

                            Assert.Equal(expectedActiveVerifier, actual);

                            return;
                        }

                        bool verified = keyRing.Verify(
                            HistoricalKeyId,
                            expectedHistoricalVerifier,
                            SessionUid,
                            AuthenticationKey
                        );

                        Assert.True(verified);
                    },
                    CancellationToken
                );
            }

            await Task.WhenAll(operations);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(activeVerificationKey);
            CryptographicOperations.ZeroMemory(historicalVerificationKey);
        }
    }

    private static GameLoginTicketAuthenticationKeyRing CreateKeyRing(
        byte[] activeVerificationKey,
        byte[] historicalVerificationKey
    )
    {
        return new GameLoginTicketAuthenticationKeyRing(
            ActiveKeyId,
            [
                new KeyValuePair<ushort, byte[]>(ActiveKeyId, activeVerificationKey),
                new KeyValuePair<ushort, byte[]>(HistoricalKeyId, historicalVerificationKey),
            ]
        );
    }

    private static void ConstructAndDispose(
        ushort activeKeyId,
        IEnumerable<KeyValuePair<ushort, byte[]>> verificationKeys
    )
    {
        using GameLoginTicketAuthenticationKeyRing keyRing = new(activeKeyId, verificationKeys);
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
