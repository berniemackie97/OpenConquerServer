using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

internal static class GameLoginTicketAuthenticationKeyVerifier
{
    public const int VerificationKeySize = 32;
    public const int VerifierSize = 32;

    private static ReadOnlySpan<byte> DomainSeparator => "OpenConquer.GameLoginTicket.AuthenticationKey.v1"u8;

    public static byte[] Create(ReadOnlySpan<byte> verificationKey, uint sessionUid, uint authenticationKey)
    {
        ValidateVerificationKey(verificationKey);
        ValidateCredentials(sessionUid, authenticationKey);

        byte[] verifier = GC.AllocateUninitializedArray<byte>(VerifierSize);

        Compute(verificationKey, sessionUid, authenticationKey, verifier);

        return verifier;
    }

    public static bool Verify(ReadOnlySpan<byte> expectedVerifier, ReadOnlySpan<byte> verificationKey, uint sessionUid, uint authenticationKey)
    {
        if (expectedVerifier.Length != VerifierSize)
        {
            throw new ArgumentException($"A game-login authentication-key verifier must contain exactly {VerifierSize} bytes.", nameof(expectedVerifier));
        }

        ValidateVerificationKey(verificationKey);
        ValidateCredentials(sessionUid, authenticationKey);

        Span<byte> computedVerifier = stackalloc byte[VerifierSize];

        try
        {
            Compute(verificationKey, sessionUid, authenticationKey, computedVerifier);

            return CryptographicOperations.FixedTimeEquals(expectedVerifier, computedVerifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computedVerifier);
        }
    }

    private static void Compute(ReadOnlySpan<byte> verificationKey, uint sessionUid, uint authenticationKey, Span<byte> destination)
    {
        int payloadLength = DomainSeparator.Length + (sizeof(uint) * 2);

        Span<byte> payload = stackalloc byte[payloadLength];

        try
        {
            DomainSeparator.CopyTo(payload);

            int offset = DomainSeparator.Length;

            BinaryPrimitives.WriteUInt32BigEndian(payload[offset..], sessionUid);

            offset += sizeof(uint);

            BinaryPrimitives.WriteUInt32BigEndian(payload[offset..], authenticationKey);

            HMACSHA256.HashData(verificationKey, payload, destination);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void ValidateVerificationKey(ReadOnlySpan<byte> verificationKey)
    {
        if (verificationKey.Length != VerificationKeySize)
        {
            throw new ArgumentException($"The game-login authentication-key verification key must contain exactly {VerificationKeySize} bytes.", nameof(verificationKey));
        }
    }

    private static void ValidateCredentials(uint sessionUid, uint authenticationKey)
    {
        if (sessionUid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionUid), "A game-login authentication-key verifier requires a nonzero session UID.");
        }

        if (authenticationKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationKey), "A game-login authentication-key verifier requires a nonzero authentication key.");
        }
    }
}
