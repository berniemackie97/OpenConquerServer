

namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Derives the 16-byte RC5 credential key from the server-supplied login seed.
/// </summary>
public static class LoginCredentialKey
{
    public const int Length = 16;

    /// <summary>
    /// Derives the credential key from <paramref name="loginSeed"/>.
    /// </summary>
    public static void Derive(uint loginSeed, Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException($"Destination must contain at least {Length} bytes.", nameof(destination));
        }

        MsvcCrtRandom random = new(loginSeed);

        random.FillBytes(destination[..Length]);
    }
}
