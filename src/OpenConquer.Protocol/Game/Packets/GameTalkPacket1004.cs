using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Represents a server-to-client native 5517 MsgTalk packet.
/// </summary>
public sealed class GameTalkPacket1004 : IPacket
{
    public const ushort PacketIdentifier = 1004;
    public const int MaximumNameEncodedLength = 15;
    public const int MaximumMessageEncodedLength = byte.MaxValue;

    private const int FixedPayloadSize =
        sizeof(uint) + sizeof(ushort) + sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(uint) + sizeof(byte);
    private const int StringCount = 4;

    public GameTalkPacket1004(uint color, ushort channel, ushort style, uint identity, string sender, string recipient, string suffix, string message)
    {
        ValidateString(sender, MaximumNameEncodedLength, nameof(sender));
        ValidateString(recipient, MaximumNameEncodedLength, nameof(recipient));
        ValidateString(suffix, MaximumNameEncodedLength, nameof(suffix));
        ValidateString(message, MaximumMessageEncodedLength, nameof(message));

        Color = color;
        Channel = channel;
        Style = style;
        Identity = identity;
        Sender = sender;
        Recipient = recipient;
        Suffix = suffix;
        Message = message;
        PayloadLength = FixedPayloadSize + StringCount + TqEncoding.Ansi.GetByteCount(Sender) + TqEncoding.Ansi.GetByteCount(Recipient) +
            TqEncoding.Ansi.GetByteCount(Suffix) + TqEncoding.Ansi.GetByteCount(Message);
    }

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength { get; }
    public uint Color { get; }
    public ushort Channel { get; }
    public ushort Style { get; }
    public uint Identity { get; }
    public string Sender { get; }
    public string Recipient { get; }
    public string Suffix { get; }
    public string Message { get; }

    public void WritePayload(ref PacketWriter writer)
    {
        writer.WriteUInt32(Color);
        writer.WriteUInt16(Channel);
        writer.WriteUInt16(Style);
        writer.WriteUInt32(Identity);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteByte(StringCount);
        writer.WriteByteString(Sender);
        writer.WriteByteString(Recipient);
        writer.WriteByteString(Suffix);
        writer.WriteByteString(Message);
    }

    private static void ValidateString(string value, int maximumEncodedLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (TqEncoding.Ansi.GetByteCount(value) > maximumEncodedLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"MsgTalk field must not exceed {maximumEncodedLength} encoded bytes.");
        }
    }
}
