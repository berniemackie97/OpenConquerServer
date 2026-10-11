using System.Buffers.Binary;
using System.Text;
using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Decodes the 5517 MsgRegister request fields documented by the version-matched server reference.
/// Native client compatibility of the complete character-creation exchange remains unverified.
/// </summary>
public readonly record struct GameCharacterCreationRequest(bool CancelRequested, string Name, ushort Body, ushort Profession, string ClientReportedMacAddress)
{
    public const ushort PacketId = 1001;
    public const int MinimumPacketLength = 92;

    private const int CancelOffset = 4;
    private const int NameOffset = 24;
    private const int NameLength = 15;
    private const int BodyOffset = 72;
    private const int ProfessionOffset = 74;
    private const int MacAddressOffset = 80;
    private const int MacAddressLength = 12;

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

        if (frame.Header.Length < MinimumPacketLength || frame.Header.Length > GameWireProtocol.MaximumPacketLength)
        {
            error = GameCharacterCreationParseError.InvalidLength;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;

        try
        {
            PacketReader nameReader = new(packet.Slice(NameOffset, NameLength));
            PacketReader macAddressReader = new(packet.Slice(MacAddressOffset, MacAddressLength));

            string name = nameReader.ReadFixedString(NameLength, WireTextEncoding.StrictAnsi);
            string macAddress = macAddressReader.ReadFixedString(MacAddressLength, WireTextEncoding.StrictAnsi);

            request = new GameCharacterCreationRequest(packet[CancelOffset] != 0, name, BinaryPrimitives.ReadUInt16LittleEndian(packet[BodyOffset..]), BinaryPrimitives.ReadUInt16LittleEndian(packet[ProfessionOffset..]), macAddress);

            return true;
        }
        catch (DecoderFallbackException)
        {
            error = GameCharacterCreationParseError.InvalidTextEncoding;
            return false;
        }
    }
}
