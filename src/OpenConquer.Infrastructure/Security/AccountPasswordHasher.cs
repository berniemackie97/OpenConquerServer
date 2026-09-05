using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Security;

/// <summary>
/// Stores and verifies account passwords.
/// </summary>
public sealed class AccountPasswordHasher : IAccountPasswordHasher
{
    private const string CurrentSchemePrefix = "$openconquer$pbkdf2-sha256$v=1$";

    private const string IdentityV3Prefix = "$openconquer$identity-v3$";
    private const int IdentityV3IterationCount = 220_000;
    private const byte IdentityV3FormatMarker = 1;
    private const uint IdentityHmacSha512Prf = 2;
    private const int IdentityHeaderLength = 13;
    private const int IdentityV3EncodedLength = 84;

    private const int IterationCount = 600_000;
    private const int SaltLength = 16;
    private const int DerivedKeyLength = 32;
    private const int SaltBase64Length = 24;
    private const int DerivedKeyBase64Length = 44;

    private static ReadOnlySpan<byte> DecoySalt =>
    [
        0x7A, 0x5C, 0x93, 0xE1, 0xD4, 0x7F, 0x2B, 0x68,
        0x8A, 0x10, 0x36, 0x5D, 0xB9, 0xC4, 0xF0, 0x21,
    ];

    private static ReadOnlySpan<byte> DecoyDerivedKey =>
    [
        0x6B, 0x4B, 0x2B, 0x60, 0xB4, 0x34, 0xDD, 0x5A,
        0xD3, 0x4D, 0x7F, 0x79, 0xED, 0x74, 0x1E, 0xA6,
        0x1F, 0x8F, 0x48, 0x10, 0xFE, 0xE6, 0xAD, 0x12,
        0x5A, 0xFF, 0x46, 0xE1, 0xA7, 0xBF, 0x8F, 0x06,
    ];

    public string HashPassword(ReadOnlySpan<char> password)
    {
        Span<byte> salt = stackalloc byte[SaltLength];
        Span<byte> derivedKey = stackalloc byte[DerivedKeyLength];

        try
        {
            RandomNumberGenerator.Fill(salt);
            DeriveKey(password, salt, derivedKey);
            string encodedSalt = Convert.ToBase64String(salt);
            string encodedDerivedKey = Convert.ToBase64String(derivedKey);
            return string.Concat(CurrentSchemePrefix, encodedSalt, "$", encodedDerivedKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public AccountPasswordVerificationStatus VerifyPassword(string passwordHash, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        return VerifyCore(passwordHash, password);
    }

    public void VerifyDecoy(ReadOnlySpan<char> password)
    {
        _ = VerifyCore(string.Empty, password);
    }

    private static AccountPasswordVerificationStatus VerifyCore(string passwordHash, ReadOnlySpan<char> password)
    {
        Span<byte> salt = stackalloc byte[SaltLength];
        Span<byte> expectedDerivedKey = stackalloc byte[DerivedKeyLength];
        Span<byte> actualDerivedKey = stackalloc byte[DerivedKeyLength];

        try
        {
            bool current = TryDecodeCurrentScheme(passwordHash, salt, expectedDerivedKey);

            if (!current)
            {
                DecoySalt.CopyTo(salt);
                DecoyDerivedKey.CopyTo(expectedDerivedKey);
            }

            DeriveKey(password, salt, actualDerivedKey);
            bool currentMatches = CryptographicOperations.FixedTimeEquals(actualDerivedKey, expectedDerivedKey);

            bool identityV3 = TryDecodeIdentityV3(passwordHash, salt, expectedDerivedKey);

            if (!identityV3)
            {
                DecoySalt.CopyTo(salt);
                DecoyDerivedKey.CopyTo(expectedDerivedKey);
            }

            Rfc2898DeriveBytes.Pbkdf2(password, salt, actualDerivedKey, IdentityV3IterationCount, HashAlgorithmName.SHA512);
            bool identityV3Matches = CryptographicOperations.FixedTimeEquals(actualDerivedKey, expectedDerivedKey);

            if (current && currentMatches)
            {
                return AccountPasswordVerificationStatus.Success;
            }

            return identityV3 && identityV3Matches
                ? AccountPasswordVerificationStatus.SuccessRehashNeeded
                : AccountPasswordVerificationStatus.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedDerivedKey);
            CryptographicOperations.ZeroMemory(actualDerivedKey);
        }
    }

    private static bool TryDecodeIdentityV3(string passwordHash, Span<byte> salt, Span<byte> derivedKey)
    {
        if (passwordHash.Length != IdentityV3Prefix.Length + IdentityV3EncodedLength || !passwordHash.StartsWith(IdentityV3Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[IdentityHeaderLength + SaltLength + DerivedKeyLength];

        try
        {
            if (!Convert.TryFromBase64Chars(passwordHash.AsSpan(IdentityV3Prefix.Length), decoded, out int written) ||
                written != decoded.Length || decoded[0] != IdentityV3FormatMarker ||
                BinaryPrimitives.ReadUInt32BigEndian(decoded[1..]) != IdentityHmacSha512Prf ||
                BinaryPrimitives.ReadUInt32BigEndian(decoded[5..]) != IdentityV3IterationCount ||
                BinaryPrimitives.ReadUInt32BigEndian(decoded[9..]) != SaltLength)
            {
                return false;
            }

            decoded.Slice(IdentityHeaderLength, SaltLength).CopyTo(salt);
            decoded[(IdentityHeaderLength + SaltLength)..].CopyTo(derivedKey);

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static void DeriveKey(ReadOnlySpan<char> password, ReadOnlySpan<byte> salt, Span<byte> destination)
    {
        Rfc2898DeriveBytes.Pbkdf2(password, salt, destination, IterationCount, HashAlgorithmName.SHA256);
    }

    private static bool TryDecodeCurrentScheme(string passwordHash, Span<byte> salt, Span<byte> derivedKey)
    {
        if (!passwordHash.StartsWith(CurrentSchemePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> encoded = passwordHash.AsSpan(CurrentSchemePrefix.Length);

        const int expectedEncodedLength = SaltBase64Length + 1 + DerivedKeyBase64Length;

        if (encoded.Length != expectedEncodedLength)
        {
            return false;
        }

        if (encoded[SaltBase64Length] != '$')
        {
            return false;
        }

        ReadOnlySpan<char> encodedSalt = encoded[..SaltBase64Length];
        ReadOnlySpan<char> encodedDerivedKey = encoded[(SaltBase64Length + 1)..];

        if (!Convert.TryFromBase64Chars(encodedSalt, salt, out int saltBytesWritten) || saltBytesWritten != SaltLength)
        {
            return false;
        }

        if (!Convert.TryFromBase64Chars(encodedDerivedKey, derivedKey, out int derivedKeyBytesWritten) || derivedKeyBytesWritten != DerivedKeyLength)
        {
            return false;
        }

        return true;
    }
}
