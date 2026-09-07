using System.Security.Cryptography;

namespace OpenConquer.Infrastructure.Security.Accounts.GameLogin;

internal sealed class GameLoginTicketAuthenticationKeyRing : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<ushort, byte[]> _verificationKeys;

    private bool _disposed;

    public GameLoginTicketAuthenticationKeyRing(ushort activeKeyId, IEnumerable<KeyValuePair<ushort, byte[]>> verificationKeys)
    {
        if (activeKeyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeKeyId), "The active game-login authentication-key verification key ID must be nonzero.");
        }

        ArgumentNullException.ThrowIfNull(verificationKeys);

        Dictionary<ushort, byte[]> ownedVerificationKeys = [];

        try
        {
            foreach (KeyValuePair<ushort, byte[]> candidate in verificationKeys)
            {
                if (candidate.Key == 0)
                {
                    throw new ArgumentException("A game-login authentication-key verification key ID must be nonzero.", nameof(verificationKeys));
                }

                byte[]? sourceVerificationKey = candidate.Value;

                if (sourceVerificationKey is null)
                {
                    throw new ArgumentException($"Game-login authentication-key verification key ID {candidate.Key} does not contain key material.", nameof(verificationKeys));
                }

                if (sourceVerificationKey.Length != GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize)
                {
                    throw new ArgumentException($"Game-login authentication-key verification key ID {candidate.Key} must contain exactly {GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize} bytes.", nameof(verificationKeys));
                }

                byte[] ownedVerificationKey = GC.AllocateUninitializedArray<byte>(GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize);

                sourceVerificationKey.AsSpan().CopyTo(ownedVerificationKey);

                if (!ownedVerificationKeys.TryAdd(candidate.Key, ownedVerificationKey))
                {
                    CryptographicOperations.ZeroMemory(ownedVerificationKey);

                    throw new ArgumentException($"Game-login authentication-key verification key ID {candidate.Key} is configured more than once.", nameof(verificationKeys));
                }
            }

            if (!ownedVerificationKeys.ContainsKey(activeKeyId))
            {
                throw new ArgumentException($"The active game-login authentication-key verification key ID {activeKeyId} is not configured.", nameof(activeKeyId));
            }

            ActiveKeyId = activeKeyId;
            _verificationKeys = ownedVerificationKeys;
        }
        catch
        {
            ZeroVerificationKeys(ownedVerificationKeys);

            throw;
        }
    }

    public ushort ActiveKeyId { get; }

    public byte[] CreateVerifier(uint sessionUid, uint authenticationKey)
    {
        Span<byte> verificationKey = stackalloc byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];

        try
        {
            CopyVerificationKey(ActiveKeyId, verificationKey);

            return GameLoginTicketAuthenticationKeyVerifier.Create(verificationKey, sessionUid, authenticationKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    public bool Verify(ushort verificationKeyId, ReadOnlySpan<byte> expectedVerifier, uint sessionUid, uint authenticationKey)
    {
        if (verificationKeyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(verificationKeyId), "A game-login authentication-key verification key ID must be nonzero.");
        }

        Span<byte> verificationKey = stackalloc byte[GameLoginTicketAuthenticationKeyVerifier.VerificationKeySize];

        try
        {
            CopyVerificationKey(verificationKeyId, verificationKey);

            return GameLoginTicketAuthenticationKeyVerifier.Verify(expectedVerifier, verificationKey, sessionUid, authenticationKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            ZeroVerificationKeys(_verificationKeys);
        }
    }

    private void CopyVerificationKey(ushort verificationKeyId, Span<byte> destination)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (!_verificationKeys.TryGetValue(verificationKeyId, out byte[]? verificationKey))
            {
                throw new KeyNotFoundException($"Game-login authentication-key verification key ID {verificationKeyId} is not configured.");
            }

            verificationKey.AsSpan().CopyTo(destination);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GameLoginTicketAuthenticationKeyRing));
        }
    }

    private static void ZeroVerificationKeys(Dictionary<ushort, byte[]> verificationKeys)
    {
        foreach (byte[] verificationKey in verificationKeys.Values)
        {
            CryptographicOperations.ZeroMemory(verificationKey);
        }

        verificationKeys.Clear();
    }
}
