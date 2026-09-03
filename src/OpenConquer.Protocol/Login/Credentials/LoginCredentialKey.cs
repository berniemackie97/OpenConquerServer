using OpenConquer.Protocol.Compatibility;

namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Derives the 16-byte RC5 credential key used by the native 5517 login
/// credential envelope from the server-supplied login seed.
/// </summary>
public static class LoginCredentialKey
{
    public const int Length = 16;

    /// <summary>
    /// Derives the native 5517 credential key from
    /// <paramref name="loginSeed"/>.
    /// </summary>
    public static void Derive(uint loginSeed, Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException(
                $"Destination must contain at least {Length} bytes.",
                nameof(destination)
            );
        }

        MsvcCrtRandom random = new(loginSeed);

        random.FillBytes(destination[..Length]);
    }
}
