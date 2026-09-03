using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Credentials;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the native standard account-login packet 1060.
/// </summary>
public static class LoginAccountRequestPacket
{
    public const ushort PacketIdentifier = 1060;

    public const int AccountNameOffset = 0;
    public const int AccountNameLength = 128;

    public const int CredentialFieldOffset = AccountNameOffset + AccountNameLength;

    public const int CredentialFieldLength = 128;

    public const int StandardCredentialTransformLength = 32;

    public const int ServerNameOffset = CredentialFieldOffset + CredentialFieldLength;

    public const int ServerNameLength = 16;

    public const int PayloadLength = AccountNameLength + CredentialFieldLength + ServerNameLength;

    public const int FrameLength = WireFrameHeader.Size + PayloadLength;

    /// <summary>
    /// Decodes the default retail 5517 packet-1060 credential envelope.
    /// </summary>
    /// <remarks>
    /// Only the first 32 credential bytes are transformed by the default
    /// shipped 5517 path. The remaining 96 bytes are intentionally ignored
    /// rather than treated as a validity condition.
    /// </remarks>
    public static bool TryDecodeStandard5517(
        ReadOnlySpan<byte> payload,
        uint loginSeed,
        out LoginAccountRequest? request
    )
    {
        request = null;

        if (payload.Length != PayloadLength)
        {
            return false;
        }

        ReadOnlySpan<byte> accountNameBytes = ReadNullTerminatedField(
            payload.Slice(AccountNameOffset, AccountNameLength)
        );

        ReadOnlySpan<byte> serverNameBytes = ReadNullTerminatedField(
            payload.Slice(ServerNameOffset, ServerNameLength)
        );

        string accountName = DecodeAnsi(accountNameBytes);

        string serverName = DecodeAnsi(serverNameBytes);

        Span<byte> credential = stackalloc byte[StandardCredentialTransformLength];

        Span<byte> key = stackalloc byte[LoginCredentialKey.Length];

        char[] password = new char[StandardCredentialTransformLength];

        bool passwordOwnershipTransferred = false;

        try
        {
            payload
                .Slice(CredentialFieldOffset, StandardCredentialTransformLength)
                .CopyTo(credential);

            LoginCredentialKey.Derive(loginSeed, key);

            LoginCredentialRc5Cipher.Decrypt(key, credential);

            int passwordLength = LoginPasswordKeypadDecoder.Decode(
                accountNameBytes,
                credential,
                password
            );

            request = new LoginAccountRequest(accountName, serverName, password, passwordLength);

            passwordOwnershipTransferred = true;

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);

            CryptographicOperations.ZeroMemory(credential);

            if (!passwordOwnershipTransferred)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            }
        }
    }

    private static ReadOnlySpan<byte> ReadNullTerminatedField(ReadOnlySpan<byte> field)
    {
        int terminatorIndex = field.IndexOf((byte)0);

        return terminatorIndex < 0 ? field : field[..terminatorIndex];
    }

    private static string DecodeAnsi(ReadOnlySpan<byte> value)
    {
        return TqEncoding.Resolve(TqTextEncoding.Ansi).GetString(value);
    }
}
