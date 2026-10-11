using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes a social relation packet (1019).
/// </summary>
public sealed class GameSocialRelationPacket(uint entityId, GameSocialRelationAction action, byte stateFlag, uint peerageRank, uint peerageSex, string name) : IPacket
{
    public ushort PacketId => GameSocialRelation.PacketIdentifier;
    public int PayloadLength => GameSocialRelation.PayloadSize;
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
            throw new InvalidOperationException($"Social-relation action {(byte)Action} is not a defined social-relation action.");
        }

        int start = writer.Written;

        writer.WriteUInt32(EntityId);
        writer.WriteByte((byte)Action);
        writer.WriteByte(StateFlag);
        writer.Reserve(2);
        writer.WriteUInt32(PeerageRank);
        writer.WriteUInt32(PeerageSex);
        writer.WriteFixedString(Name, GameSocialRelation.MaximumNameEncodedLength, WireTextEncoding.StrictAnsi);
        writer.Reserve(1);

        int payloadLength = writer.Written - start;
        if (payloadLength != GameSocialRelation.PayloadSize)
        {
            throw new InvalidOperationException($"Social relation payload must be exactly {GameSocialRelation.PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
