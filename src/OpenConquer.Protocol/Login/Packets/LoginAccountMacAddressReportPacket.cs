using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the client -> AccountServer post authentication MAC address report packet 1100.
/// </summary>
public static class LoginAccountMacAddressReportPacket
{
    public const ushort PacketIdentifier = 1100;
    public const int SessionUidOffset = 0;
    public const int MacAddressOffset = SessionUidOffset + sizeof(uint);
    public const int MacAddressFieldLength = 40;
    public const int TrailingBytesOffset = MacAddressOffset + MacAddressFieldLength;
    public const int TrailingBytesLength = 4;
    public const int PayloadLength = sizeof(uint) + MacAddressFieldLength + TrailingBytesLength;
    public const int FrameLength = WireFrameHeader.Size + PayloadLength;

    public static bool TryDecode(ReadOnlySpan<byte> payload, out LoginAccountMacAddressReport? report)
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
