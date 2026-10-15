using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Strict inbound 5517 MsgTalk (1004) payload. Sender, identity, colour, style, and
/// suffix are deliberately not authoritative; the world session supplies them.
/// </summary>
public readonly record struct GameInboundChat(ushort Channel, string Recipient, string Message)
{
    public const ushort LocalChannel = 0x7D7;
    public const ushort LegacyLocalChannel = 0x7D0;
    public const ushort WhisperChannel = 0x7D2;
    public const ushort RoleWordChannel = 0x838;
    public const ushort AlternateRoleWordChannel = 0x836;
    public const int MaximumRoleWordBytes = 0x40;
    public const string BroadcastRecipient = "ALL";

    private const int StringListOffset = 24;
    private const int MaximumFrameLength = 25 + 4 + 3 * GameChatPacket.MaximumNameEncodedLength + GameChatPacket.MaximumMessageEncodedLength;

    public bool IsLocal => Channel is LocalChannel or LegacyLocalChannel;
    public bool IsWhisper => Channel == WhisperChannel;
    public bool IsRoleWord => Channel is RoleWordChannel or AlternateRoleWordChannel;
    public bool IsSupported => IsLocal || IsWhisper || IsRoleWord;

    public static bool TryParse(GameInboundFrame frame, out GameInboundChat request)
    {
        ArgumentNullException.ThrowIfNull(frame);
        request = default;
        if (frame.PacketId != GameChatPacket.PacketIdentifier ||
            frame.Header.Length is < 29 or > MaximumFrameLength)
        {
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;
        if (packet[StringListOffset] != 4)
        {
            return false;
        }

        int offset = StringListOffset + 1;
        string recipient = string.Empty;
        string message = string.Empty;
        for (int i = 0; i < 4; i++)
        {
            if (offset >= packet.Length)
            {
                return false;
            }

            int size = packet[offset++];
            int maximum = i == 3 ? GameChatPacket.MaximumMessageEncodedLength : GameChatPacket.MaximumNameEncodedLength;
            if (size > maximum || size > packet.Length - offset)
            {
                return false;
            }

            ReadOnlySpan<byte> encoded = packet.Slice(offset, size);
            if (encoded.Contains((byte)0))
            {
                return false;
            }

            // Only materialize fields used for routing and presentation. Forged
            // identity, sender, suffix, colour, and style never leave this parser.
            if (i == 1)
            {
                recipient = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetString(encoded);
            }
            else if (i == 3)
            {
                message = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetString(encoded);
            }
            offset += size;
        }

        if (offset != packet.Length || recipient.Length == 0)
        {
            return false;
        }

        request = new GameInboundChat(BinaryPrimitives.ReadUInt16LittleEndian(packet[8..]), recipient,
            message.Replace('\r', ' ').Replace('\n', ' '));
        return true;
    }
}
