using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Credentials;

/// <summary>
/// Decodes the account-name field and the RC5/keypad password envelope shared by the login request and the
/// project-owned registration request.
/// </summary>
internal static class LoginCredentialEnvelope
{
    public const int AccountNameLength = 128;
    public const int CredentialFieldLength = 128;
    public const int TransformLength = 32;

    /// <remarks>
    /// Only the first <see cref="TransformLength"/> credential bytes are encrypted by the client. The remaining
    /// bytes are ignored rather than treated as a validity condition.
    /// </remarks>
    public static void Decode(ReadOnlySpan<byte> accountField, ReadOnlySpan<byte> credentialField, uint loginSeed,
        out string accountName, out char[] password, out int passwordLength)
    {
        if (accountField.Length != AccountNameLength || credentialField.Length != CredentialFieldLength)
        {
            throw new ArgumentException("Credential envelope fields have the wrong length.");
        }

        ReadOnlySpan<byte> accountNameBytes = ReadNullTerminatedField(accountField);
        accountName = DecodeAnsi(accountNameBytes);

        Span<byte> credential = stackalloc byte[TransformLength];
        Span<byte> key = stackalloc byte[LoginCredentialKey.Length];
        char[] decoded = new char[TransformLength];
        bool ownershipTransferred = false;

        try
        {
            credentialField[..TransformLength].CopyTo(credential);
            LoginCredentialKey.Derive(loginSeed, key);
            LoginCredentialRc5Cipher.Decrypt(key, credential);
            passwordLength = LoginPasswordKeypadDecoder.Decode(accountNameBytes, credential, decoded);
            password = decoded;
            ownershipTransferred = true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(credential);

            if (!ownershipTransferred)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(decoded.AsSpan()));
            }
        }
    }

    public static ReadOnlySpan<byte> ReadNullTerminatedField(ReadOnlySpan<byte> field)
    {
        int terminatorIndex = field.IndexOf((byte)0);

        return terminatorIndex < 0 ? field : field[..terminatorIndex];
    }

    public static string DecodeAnsi(ReadOnlySpan<byte> value)
    {
        return WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetString(value);
    }
}
