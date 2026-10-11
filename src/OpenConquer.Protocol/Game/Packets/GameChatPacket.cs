using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Server-to-client chat message (packet 1004).
/// </summary>
public sealed class GameChatPacket : IPacket
{
    public const ushort PacketIdentifier = 1004;
    public const int MaximumNameEncodedLength = 15;
    public const int MaximumMessageEncodedLength = byte.MaxValue;

    private const int FixedPayloadSize = sizeof(uint) + sizeof(ushort) + sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(uint) + sizeof(byte);
    private const byte StringCount = 4;

    public GameChatPacket(uint color, ushort channel, ushort style, uint identity, string sender, string recipient, string suffix, string message)
    {
        int senderLength = GetValidatedEncodedLength(sender, MaximumNameEncodedLength, nameof(sender));
        int recipientLength = GetValidatedEncodedLength(recipient, MaximumNameEncodedLength, nameof(recipient));
        int suffixLength = GetValidatedEncodedLength(suffix, MaximumNameEncodedLength, nameof(suffix));
        int messageLength = GetValidatedEncodedLength(message, MaximumMessageEncodedLength, nameof(message));

        Color = color;
        Channel = channel;
        Style = style;
        Identity = identity;
        Sender = sender;
        Recipient = recipient;
        Suffix = suffix;
        Message = message;
        PayloadLength = FixedPayloadSize + StringCount + senderLength + recipientLength + suffixLength + messageLength;
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

    private static int GetValidatedEncodedLength(string value, int maximumEncodedLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        int encodedLength = WireTextEncodingResolver.Resolve(WireTextEncoding.Ansi).GetByteCount(value);

        if (encodedLength > maximumEncodedLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Chat field must not exceed {maximumEncodedLength} encoded bytes.");
        }

        return encodedLength;
    }
}
