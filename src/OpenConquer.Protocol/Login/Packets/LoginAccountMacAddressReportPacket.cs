using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the native client-to-AccountServer
/// post-authentication MAC-address report packet 1100.
/// </summary>
public static class LoginAccountMacAddressReportPacket
{
    public const ushort PacketIdentifier = 1100;

    public const int SessionUidOffset = 0;

    public const int MacAddressOffset = SessionUidOffset + sizeof(uint);

    public const int MacAddressFieldLength = 40;

    /// <summary>
    /// Offset of the four trailing bytes present in the verified 52-byte
    /// packet-1100 frame but not assigned a protocol meaning by current native
    /// evidence.
    /// </summary>
    public const int TrailingBytesOffset = MacAddressOffset + MacAddressFieldLength;

    public const int TrailingBytesLength = 4;

    public const int PayloadLength = sizeof(uint) + MacAddressFieldLength + TrailingBytesLength;

    public const int FrameLength = WireFrameHeader.Size + PayloadLength;

    /// <summary>
    /// Decodes the native structural packet-1100 payload.
    /// </summary>
    /// <remarks>
    /// MAC-address formatting, expected-session correlation, and the contents
    /// of the semantically unspecified trailing bytes are AccountServer policy
    /// or compatibility concerns and are intentionally not enforced here.
    /// </remarks>
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out LoginAccountMacAddressReport? report
    )
    {
        report = null;

        if (payload.Length != PayloadLength)
        {
            return false;
        }

        PacketReader reader = new(payload);

        uint sessionUid = reader.ReadUInt32();

        string macAddress = reader.ReadFixedString(MacAddressFieldLength, TqTextEncoding.Ascii);

        _ = reader.ReadBytes(TrailingBytesLength);

        report = new LoginAccountMacAddressReport(sessionUid, macAddress);

        return true;
    }
}
