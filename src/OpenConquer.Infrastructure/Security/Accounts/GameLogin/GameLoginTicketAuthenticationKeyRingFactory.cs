using System.Security.Cryptography;

namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

internal static class GameLoginTicketAuthenticationKeyRingFactory
{
    private const int EncodedVerificationKeySize = 44;

    internal static void ValidateConfiguration(ushort activeKeyId, IEnumerable<KeyValuePair<ushort, string>> encodedVerificationKeys)
    {
        using GameLoginTicketAuthenticationKeyRing validationRing = Create(activeKeyId, encodedVerificationKeys);
    }

    public static GameLoginTicketAuthenticationKeyRing Create(ushort activeKeyId, IEnumerable<KeyValuePair<ushort, string>> encodedVerificationKeys)
    {
        if (activeKeyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeKeyId), "The active game-login authentication-key verification key ID must be nonzero.");
        }

        ArgumentNullException.ThrowIfNull(encodedVerificationKeys);

        List<KeyValuePair<ushort, byte[]>> decodedVerificationKeys = [];
        HashSet<ushort> configuredKeyIds = [];

        bool activeKeyConfigured = false;

        try
        {
            foreach (KeyValuePair<ushort, string> candidate in encodedVerificationKeys)
            {
                if (candidate.Key == 0)
                {
                    throw new ArgumentException("A game-login authentication-key verification key ID must be nonzero.", nameof(encodedVerificationKeys));
                }

                if (!configuredKeyIds.Add(candidate.Key))
                {
                    throw new ArgumentException($"Game-login authentication-key verification key ID {candidate.Key} is configured more than once.", nameof(encodedVerificationKeys));
                }

                if (candidate.Key == activeKeyId)
                {
                    activeKeyConfigured = true;
                }

                byte[] decodedVerificationKey = DecodeVerificationKey(candidate.Key, candidate.Value, nameof(encodedVerificationKeys));

                decodedVerificationKeys.Add(new KeyValuePair<ushort, byte[]>(candidate.Key, decodedVerificationKey));
            }

            if (!activeKeyConfigured)
            {
                throw new ArgumentException($"The active game-login authentication-key verification key ID {activeKeyId} is not configured.", nameof(activeKeyId));
            }

            return new GameLoginTicketAuthenticationKeyRing(activeKeyId, decodedVerificationKeys);
        }
        finally
        {
            foreach (KeyValuePair<ushort, byte[]> candidate in decodedVerificationKeys)
            {
                CryptographicOperations.ZeroMemory(candidate.Value);
            }
        }
    }

    private static byte[] DecodeVerificationKey(ushort verificationKeyId, string? encodedVerificationKey, string parameterName)
    {
        if (encodedVerificationKey is null)
        {
            throw new ArgumentException($"Game-login authentication-key verification key ID {verificationKeyId} does not contain encoded key material.", parameterName);
        }

        if (encodedVerificationKey.Length != EncodedVerificationKeySize)
        {
            throw CreateInvalidEncodingException(verificationKeyId, parameterName);
        }

        Span<byte> decodedVerificationKey = stackalloc byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];
        Span<char> canonicalEncoding = stackalloc char[EncodedVerificationKeySize];

        try
        {
            bool decoded = Convert.TryFromBase64String(encodedVerificationKey, decodedVerificationKey, out int bytesWritten);

            if (!decoded || bytesWritten != GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize)
            {
                throw CreateInvalidEncodingException(verificationKeyId, parameterName);
            }

            bool encoded = Convert.TryToBase64Chars(decodedVerificationKey, canonicalEncoding, out int charactersWritten);

            if (!encoded || charactersWritten != EncodedVerificationKeySize || !encodedVerificationKey.AsSpan().SequenceEqual(canonicalEncoding))
            {
                throw CreateInvalidEncodingException(verificationKeyId, parameterName);
            }

            byte[] ownedVerificationKey = GC.AllocateUninitializedArray<byte>(GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize);

            decodedVerificationKey.CopyTo(ownedVerificationKey);

            return ownedVerificationKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodedVerificationKey);
            canonicalEncoding.Clear();
        }
    }

    private static ArgumentException CreateInvalidEncodingException(ushort verificationKeyId, string parameterName)
    {
        return new ArgumentException($"Game-login authentication-key verification key ID {verificationKeyId} must be the canonical Base64 encoding of exactly {GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize} bytes.", parameterName);
    }
}
