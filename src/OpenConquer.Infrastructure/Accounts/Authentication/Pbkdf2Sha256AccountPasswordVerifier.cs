using System.Security.Cryptography;
using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Infrastructure.Accounts.Authentication;

/// <summary>
/// Stores and verifies account passwords using the version-1 OpenConquer
/// PBKDF2-HMAC-SHA256 password-storage scheme.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 fixes the complete KDF contract:
/// PBKDF2-HMAC-SHA256, 600,000 iterations, a 16-byte random salt, and a
/// 32-byte derived key.
/// </para>
/// <para>
/// Cost parameters are intentionally identified by scheme version rather than
/// accepted from persisted input. A future password-storage scheme can retain
/// version-1 verification and return
/// <see cref="AccountPasswordVerificationStatus.SuccessRehashNeeded"/> after a
/// successful legacy verification.
/// </para>
/// </remarks>
public sealed class Pbkdf2Sha256AccountPasswordVerifier : IAccountPasswordVerifier
{
    private const string CurrentSchemePrefix = "$openconquer$pbkdf2-sha256$v=1$";

    private const int IterationCount = 600_000;
    private const int SaltLength = 16;
    private const int DerivedKeyLength = 32;
    private const int SaltBase64Length = 24;
    private const int DerivedKeyBase64Length = 44;

    // These values are not credentials and do not require secrecy. They exist
    // solely so account misses and malformed persisted hashes still perform a
    // complete current-cost password derivation before failing.
    private static ReadOnlySpan<byte> DecoySalt =>
        [
            0x7A,
            0x5C,
            0x93,
            0xE1,
            0xD4,
            0x7F,
            0x2B,
            0x68,
            0x8A,
            0x10,
            0x36,
            0x5D,
            0xB9,
            0xC4,
            0xF0,
            0x21,
        ];

    private static ReadOnlySpan<byte> DecoyDerivedKey =>
        [
            0x6B,
            0x4B,
            0x2B,
            0x60,
            0xB4,
            0x34,
            0xDD,
            0x5A,
            0xD3,
            0x4D,
            0x7F,
            0x79,
            0xED,
            0x74,
            0x1E,
            0xA6,
            0x1F,
            0x8F,
            0x48,
            0x10,
            0xFE,
            0xE6,
            0xAD,
            0x12,
            0x5A,
            0xFF,
            0x46,
            0xE1,
            0xA7,
            0xBF,
            0x8F,
            0x06,
        ];

    public AccountPasswordVerificationStatus VerifyPassword(string passwordHash, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        Span<byte> salt = stackalloc byte[SaltLength];
        Span<byte> expectedDerivedKey = stackalloc byte[DerivedKeyLength];

        if (!TryDecodeCurrentScheme(passwordHash, salt, expectedDerivedKey))
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedDerivedKey);

            VerifyDecoy(password);

            return AccountPasswordVerificationStatus.Failed;
        }

        Span<byte> actualDerivedKey = stackalloc byte[DerivedKeyLength];

        try
        {
            DeriveKey(password, salt, actualDerivedKey);

            bool matches = CryptographicOperations.FixedTimeEquals(actualDerivedKey, expectedDerivedKey);

            return matches ? AccountPasswordVerificationStatus.Success : AccountPasswordVerificationStatus.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedDerivedKey);
            CryptographicOperations.ZeroMemory(actualDerivedKey);
        }
    }

    public void VerifyDecoy(ReadOnlySpan<char> password)
    {
        Span<byte> derivedKey = stackalloc byte[DerivedKeyLength];

        try
        {
            DeriveKey(password, DecoySalt, derivedKey);

            _ = CryptographicOperations.FixedTimeEquals(derivedKey, DecoyDerivedKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

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
