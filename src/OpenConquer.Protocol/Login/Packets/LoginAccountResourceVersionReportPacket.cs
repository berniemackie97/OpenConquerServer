using OpenConquer.Protocol.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Login.Packets;

/// <summary>
/// Defines and decodes the client -> AccountServer resource version report packet 1052.
/// </summary>
public static class LoginAccountResourceVersionReportPacket
{
    public const ushort PacketIdentifier = 1052;

    public const int SessionUidOffset = 0;
    public const int ResourceVersionOffset = SessionUidOffset + sizeof(uint);
    public const int ResourceNameOffset = ResourceVersionOffset + sizeof(int);
    public const int ResourceNameFieldLength = 16;
    public const int PayloadLength = sizeof(uint) + sizeof(int) + ResourceNameFieldLength;
    public const int FrameLength = WireFrameHeader.Size + PayloadLength;

    public static bool TryDecode(ReadOnlySpan<byte> payload, out LoginAccountResourceVersionReport? report)
    {
        report = null;

        if (payload.Length != PayloadLength)
        {
            return false;
        }

        PacketReader reader = new(payload);

        uint sessionUid = reader.ReadUInt32();

        int resourceVersion = unchecked((int)reader.ReadUInt32());

        string resourceName = reader.ReadFixedString(ResourceNameFieldLength, TqTextEncoding.Ascii);

        report = new LoginAccountResourceVersionReport(sessionUid, resourceVersion, resourceName);

        return true;
    }
}
