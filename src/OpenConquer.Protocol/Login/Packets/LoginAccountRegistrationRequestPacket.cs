using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Login.Credentials;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the OpenConquer account-registration request (packet 0xF001).
/// </summary>
/// <remarks>
/// This packet is a project extension; retail 5517 registered accounts on a website. It is sent on an
/// established login connection in place of packet 1060 and carries the same account-name field and
/// RC5/keypad password envelope keyed by that connection's seed. There is no server-name field:
/// accounts are not bound to a realm.
/// <code>
/// +0x00 char[128] account name, NUL-terminated ANSI
/// +0x80 byte[128] password envelope; only the first 32 bytes are encrypted
/// </code>
/// </remarks>
public static class LoginAccountRegistrationRequestPacket
{
    public const ushort PacketIdentifier = 0xF001;

    public const int AccountNameOffset = 0;
    public const int AccountNameLength = LoginCredentialEnvelope.AccountNameLength;
    public const int CredentialFieldOffset = AccountNameOffset + AccountNameLength;
    public const int CredentialFieldLength = LoginCredentialEnvelope.CredentialFieldLength;
    public const int PayloadLength = AccountNameLength + CredentialFieldLength;
    public const int FrameLength = WireFrameHeader.Size + PayloadLength;

    public static bool TryDecode(ReadOnlySpan<byte> payload, uint loginSeed, out LoginAccountRegistrationRequest? request)
    {
        request = null;

        if (payload.Length != PayloadLength)
        {
            return false;
        }

        LoginCredentialEnvelope.Decode(payload.Slice(AccountNameOffset, AccountNameLength),
            payload.Slice(CredentialFieldOffset, CredentialFieldLength), loginSeed,
            out string accountName, out char[] password, out int passwordLength);

        request = new LoginAccountRegistrationRequest(accountName, password, passwordLength);

        return true;
    }
}
