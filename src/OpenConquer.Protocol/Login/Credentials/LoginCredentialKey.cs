namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Derives the 16 byte RC5 credential key used by the native 5517 login credential envelope from the server supplied login seed.
/// </summary>
public static class LoginCredentialKey
{
    public const int Length = 16;

    private const uint Multiplier = 0x343FD;
    private const uint Increment = 0x269EC3;
    private const uint ByteModulus = 0x100;

    /// <summary>
    /// Derives the native 5517 credential key from
    /// <paramref name="loginSeed"/>.
    /// </summary>
    public static void Derive(uint loginSeed, Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException($"Destination must contain at least {Length} bytes.", nameof(destination));
        }

        uint state = loginSeed;

        for (int index = 0; index < Length; index++)
        {
            state = unchecked((state * Multiplier) + Increment);

            uint randomValue = (state >> 16) & 0x7FFF;

            destination[index] = (byte)(randomValue % ByteModulus);
        }
    }
}
