using System.Buffers.Binary;
using OpenConquer.Protocol.Game.Framing;

namespace OpenConquer.Protocol.Game.Packets;

public enum GameLoginProofParseError
{
    None = 0,
    InvalidLength,
    InvalidPacketId,
    InvalidMode,
    InvalidReservedField,
}

/// <summary>
/// Native 28-byte login proof sent by the standard 5517 client after the
/// GameServer Diffie-Hellman handshake completes.
/// </summary>
public readonly record struct GameLoginProof1052(
    uint SessionUid,
    uint AuthenticationKey,
    ushort Mode,
    ushort LocaleTag,
    ulong HardwareAddress,
    int ResourceVersion)
{
    public const int PacketLength = 28;
    public const ushort PacketId = 1052;
    public const ushort ExpectedMode = 0x7C;

    private const int SessionUidOffset = 4;
    private const int AuthenticationKeyOffset = 8;
    private const int ModeOffset = 12;
    private const int LocaleTagOffset = 14;
    private const int HardwareAddressOffset = 16;
    private const int HardwareAddressLength = 6;
    private const int ReservedOffset = 22;
    private const int ResourceVersionOffset = 24;

    public static bool TryParse(GameInboundFrame frame, out GameLoginProof1052 proof, out GameLoginProofParseError error)
    {
        ArgumentNullException.ThrowIfNull(frame);

        proof = default;
        error = GameLoginProofParseError.None;

        if (frame.Header.Length != PacketLength)
        {
            error = GameLoginProofParseError.InvalidLength;
            return false;
        }

        if (frame.PacketId != PacketId)
        {
            error = GameLoginProofParseError.InvalidPacketId;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;
        ushort mode = BinaryPrimitives.ReadUInt16LittleEndian(packet[ModeOffset..]);

        if (mode != ExpectedMode)
        {
            error = GameLoginProofParseError.InvalidMode;
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(packet[ReservedOffset..]) != 0)
        {
            error = GameLoginProofParseError.InvalidReservedField;
            return false;
        }

        ulong hardwareAddress = 0;

        for (int index = 0; index < HardwareAddressLength; index++)
        {
            hardwareAddress |= (ulong)packet[HardwareAddressOffset + index] << (index * 8);
        }

        proof = new GameLoginProof1052(
            BinaryPrimitives.ReadUInt32LittleEndian(packet[SessionUidOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[AuthenticationKeyOffset..]),
            mode,
            BinaryPrimitives.ReadUInt16LittleEndian(packet[LocaleTagOffset..]),
            hardwareAddress,
            BinaryPrimitives.ReadInt32LittleEndian(packet[ResourceVersionOffset..]));

        return true;
    }
}
