using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Social relation packet (1019) body.
/// </summary>
public readonly record struct GameSocialRelation(uint EntityId, GameSocialRelationAction Action, byte StateFlag, ushort ReservedField, uint PeerageRank, uint PeerageSex, string Name)
{
    public const ushort PacketIdentifier = 1019;
    public const int FixedPacketLength = 36;
    public const int PayloadSize = FixedPacketLength - 4;
    public const int NameFieldLength = 16;
    public const int MaximumNameEncodedLength = NameFieldLength - 1;

    private const int NameTerminatorOffset = FixedPacketLength - 1;

    public static bool TryParse(GameInboundFrame frame, out GameSocialRelation relation, out GameSocialRelationParseError error)
    {
        ArgumentNullException.ThrowIfNull(frame);

        relation = default;

        if (frame.PacketId != PacketIdentifier)
        {
            error = GameSocialRelationParseError.InvalidPacketId;
            return false;
        }

        if (frame.Header.Length != FixedPacketLength)
        {
            error = GameSocialRelationParseError.InvalidPacketLength;
            return false;
        }

        ReadOnlySpan<byte> packet = frame.Packet.Span;

        if (packet[NameTerminatorOffset] != 0)
        {
            error = GameSocialRelationParseError.MissingNameTerminator;
            return false;
        }

        PacketReader reader = new(packet[4..]);

        uint entityId = reader.ReadUInt32();
        GameSocialRelationAction action = (GameSocialRelationAction)reader.ReadByte();
        byte stateFlag = reader.ReadByte();
        ushort reservedField = reader.ReadUInt16();
        uint peerageRank = reader.ReadUInt32();
        uint peerageSex = reader.ReadUInt32();
        string name = reader.ReadFixedString(MaximumNameEncodedLength, WireTextEncoding.Ansi);
        _ = reader.ReadByte();

        relation = new GameSocialRelation(entityId, action, stateFlag, reservedField, peerageRank, peerageSex, name);
        error = GameSocialRelationParseError.None;
        return true;
    }
}
