using OpenConquer.Protocol.Game.Framing;
using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

public enum GameSocialRelationAction : byte
{
    FriendRequest = 10,
    AddFriend = 11,
    FriendOnline = 12,
    FriendOffline = 13,
    RemoveFriend = 14,
    AddFriendSilently = 15,
    EnemyOnline = 16,
    EnemyOffline = 17,
    RemoveEnemy = 18,
    AddEnemy = 19,
}

public enum GameSocialRelationParseError
{
    None = 0,
    InvalidPacketId,
    InvalidPacketLength,
    MissingNameTerminator,
}

/// <summary>
/// Represents the verified native 5517 MsgFriend packet-1019 body.
/// </summary>
public readonly record struct GameSocialRelation1019(uint EntityId, GameSocialRelationAction Action, byte StateFlag, ushort ReservedField, uint PeerageRank, uint PeerageSex, string Name)
{
    public const ushort PacketIdentifier = 1019;
    public const int FixedPacketLength = 36;
    public const int PayloadSize = FixedPacketLength - 4;
    public const int NameFieldLength = 16;
    public const int MaximumNameEncodedLength = NameFieldLength - 1;

    private const int NameTerminatorOffset = FixedPacketLength - 1;

    public static bool TryParse(GameInboundFrame frame, out GameSocialRelation1019 relation, out GameSocialRelationParseError error)
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
        string name = reader.ReadFixedString(MaximumNameEncodedLength, TqTextEncoding.Ansi);
        _ = reader.ReadByte();

        relation = new GameSocialRelation1019(entityId, action, stateFlag, reservedField, peerageRank, peerageSex, name);
        error = GameSocialRelationParseError.None;
        return true;
    }
}

/// <summary>
/// Writes the verified native 5517 MsgFriend packet-1019 layout.
/// </summary>
public sealed class GameSocialRelationPacket1019(uint entityId, GameSocialRelationAction action, byte stateFlag, uint peerageRank, uint peerageSex, string name) : IPacket
{
    public ushort PacketId => GameSocialRelation1019.PacketIdentifier;
    public int PayloadLength => GameSocialRelation1019.PayloadSize;
    public uint EntityId { get; } = entityId;
    public GameSocialRelationAction Action { get; } = action;
    public byte StateFlag { get; } = stateFlag;
    public uint PeerageRank { get; } = peerageRank;
    public uint PeerageSex { get; } = peerageSex;
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public void WritePayload(ref PacketWriter writer)
    {
        if (EntityId == 0)
        {
            throw new InvalidOperationException("A social-relation packet requires a nonzero character ID.");
        }

        if ((byte)Action is < (byte)GameSocialRelationAction.FriendRequest or > (byte)GameSocialRelationAction.AddEnemy)
        {
            throw new InvalidOperationException($"Social-relation action {(byte)Action} is not defined by the native 5517 MsgFriend contract.");
        }

        int start = writer.Written;

        writer.WriteUInt32(EntityId);
        writer.WriteByte((byte)Action);
        writer.WriteByte(StateFlag);
        writer.Reserve(2);
        writer.WriteUInt32(PeerageRank);
        writer.WriteUInt32(PeerageSex);
        writer.WriteFixedString(Name, GameSocialRelation1019.MaximumNameEncodedLength, TqTextEncoding.StrictAnsi);
        writer.Reserve(1);

        int payloadLength = writer.Written - start;
        if (payloadLength != GameSocialRelation1019.PayloadSize)
        {
            throw new InvalidOperationException($"MsgFriend payload must be exactly {GameSocialRelation1019.PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
