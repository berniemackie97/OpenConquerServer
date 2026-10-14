using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Server-to-client notice that the connection is being closed (packet 1115).
/// </summary>
/// <remarks>
/// <code>
/// +0x00 UInt32 reason
/// +0x04 UInt32 unread by the client
/// +0x08 UInt32 unread by the client
/// +0x0C char[] notice text, NUL-terminated; the client keeps at most 255 bytes
/// </code>
/// </remarks>
public sealed class GameKickoutNoticePacket : IPacket
{
    public const ushort PacketIdentifier = 1115;
    public const int MaximumTextLength = 254;

    public GameKickoutNoticePacket(GameKickoutReason reason, string text)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown kickout reason.");
        }

        ArgumentNullException.ThrowIfNull(text);

        if (text.Contains('\0'))
        {
            throw new ArgumentException("Kickout notice text must not contain embedded null characters.", nameof(text));
        }

        int textLength = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetByteCount(text);

        if (textLength > MaximumTextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(text), $"Kickout notice text must fit within {MaximumTextLength} bytes.");
        }

        Reason = reason;
        Text = text;
        PayloadLength = (sizeof(uint) * 3) + textLength + sizeof(byte);
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength { get; }
    public GameKickoutReason Reason { get; }
    public string Text { get; }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32((uint)Reason);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteNullTerminatedString(Text);
    }
}
