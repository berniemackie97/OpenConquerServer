using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Decodes the native 5517 character-creation request without authorizing or executing creation.
/// </summary>
public readonly record struct GameCharacterCreationRequest(bool CancelRequested, string Name, ushort Body, ushort Profession, uint ClientReportedSessionUid, string ClientReportedMacAddress)
{
    public const ushort PacketId = 1001;
    public const int PacketLength = 124;

    private const int CancelOffset = 4;
    private const int ReservedOffset = 8;
    private const int ReservedLength = 16;
    private const int NameOffset = 24;
    private const int NameLength = 48;
    private const int MaximumClientNameLength = 15;
    private const int BodyOffset = 72;
    private const int ProfessionOffset = 74;
    private const int SessionUidOffset = 76;
    private const int MacAddressOffset = 80;
    private const int MacAddressLength = 40;
    private const int TrailingZeroOffset = 120;

    public static bool TryParse(GameInboundFrame frame, out GameCharacterCreationRequest request, out GameCharacterCreationParseError error)
    {
        ArgumentNullException.ThrowIfNull(frame);

        request = default;
        error = GameCharacterCreationParseError.None;

        if (frame.PacketId != PacketId)
        {
            error = GameCharacterCreationParseError.InvalidPacketId;
            return false;
        }

        if (frame.Header.Length != PacketLength)
        {
            error = GameCharacterCreationParseError.InvalidLength;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;
        uint cancelFlag = BinaryPrimitives.ReadUInt32LittleEndian(packet[CancelOffset..]);

        if (cancelFlag > 1)
        {
            error = GameCharacterCreationParseError.InvalidCancelFlag;
            return false;
        }

        if (ContainsNonzero(packet.Slice(ReservedOffset, ReservedLength)) || packet[TrailingZeroOffset] != 0)
        {
            error = GameCharacterCreationParseError.InvalidReservedField;
            return false;
        }

        ReadOnlySpan<byte> nameField = packet.Slice(NameOffset, NameLength);
        int nameLength = nameField[..(MaximumClientNameLength + 1)].IndexOf((byte)0);

        if (nameLength < 0)
        {
            error = GameCharacterCreationParseError.InvalidNameField;
            return false;
        }

        ReadOnlySpan<byte> macAddressField = packet.Slice(MacAddressOffset, MacAddressLength);
        int macAddressLength = macAddressField.IndexOf((byte)0);

        if (macAddressLength < 0)
        {
            macAddressLength = macAddressField.Length;
        }

        if (ContainsUndefinedWindows1252(nameField[..nameLength]) || ContainsNonAscii(macAddressField[..macAddressLength]))
        {
            error = GameCharacterCreationParseError.InvalidTextEncoding;
            return false;
        }

        try
        {
            PacketReader nameReader = new(nameField);
            PacketReader macAddressReader = new(macAddressField);

            string name = nameReader.ReadFixedString(NameLength, WireTextEncoding.StrictAnsi);
            string macAddress = macAddressReader.ReadFixedString(MacAddressLength, WireTextEncoding.Ascii);

            request = new GameCharacterCreationRequest(
                cancelFlag != 0,
                name,
                BinaryPrimitives.ReadUInt16LittleEndian(packet[BodyOffset..]),
                BinaryPrimitives.ReadUInt16LittleEndian(packet[ProfessionOffset..]),
                BinaryPrimitives.ReadUInt32LittleEndian(packet[SessionUidOffset..]),
                macAddress);

            return true;
        }
        catch (DecoderFallbackException)
        {
            error = GameCharacterCreationParseError.InvalidTextEncoding;
            return false;
        }
    }

    private static bool ContainsNonzero(ReadOnlySpan<byte> value)
    {
        foreach (byte item in value)
        {
            if (item != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUndefinedWindows1252(ReadOnlySpan<byte> value)
    {
        foreach (byte item in value)
        {
            if (item is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> value)
    {
        foreach (byte item in value)
        {
            if (item > 0x7F)
            {
                return true;
            }
        }

        return false;
    }
}
