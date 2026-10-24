using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Credentials;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the standard account login request (packet 1060).
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
    /// Decodes the standard credential envelope.
    /// </summary>
    /// <remarks>
    /// Only the first 32 credential bytes are encrypted by the client. The
    /// remaining 96 bytes are ignored rather than treated as a validity condition.
    /// </remarks>
    public static bool TryDecodeStandard(ReadOnlySpan<byte> payload, uint loginSeed, out LoginAccountRequest? request)
    {
        request = null;

        if (payload.Length != PayloadLength)
        {
            return false;
        }

        string serverName = LoginCredentialEnvelope.DecodeAnsi(
            LoginCredentialEnvelope.ReadNullTerminatedField(payload.Slice(ServerNameOffset, ServerNameLength)));

        LoginCredentialEnvelope.Decode(payload.Slice(AccountNameOffset, AccountNameLength),
            payload.Slice(CredentialFieldOffset, CredentialFieldLength), loginSeed,
            out string accountName, out char[] password, out int passwordLength);

        request = new LoginAccountRequest(accountName, serverName, password, passwordLength);

        return true;
    }
}
