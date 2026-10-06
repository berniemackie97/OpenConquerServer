using OpenConquer.Protocol.Packets;
using OpenConquer.Protocol.Serialization;
using OpenConquer.Protocol.Text;

namespace OpenConquer.Protocol.Game.Packets;

/// <summary>
/// Writes the native-5517-compatible MsgSyndicateAttributeInfo packet-1106 layout.
/// </summary>
public sealed class GameSyndicateAttributeInfoPacket1106(uint syndicateId, uint memberProffer, ulong silverFund, uint emoneyFund,
    uint population, uint memberRank, string leaderName, uint requiredLevel, uint requiredMetempsychosis, uint requiredProfession,
    byte syndicateLevel, ushort mantle, uint positionExpirationDate, uint joinDate, string syndicateName) : IPacket
{
    public const ushort PacketIdentifier = 1106;
    public const int FixedPacketLength = 92;
    public const int PayloadSize = FixedPacketLength - 4;
    public const int LeaderNameFieldLength = 16;
    public const int MaximumLeaderNameEncodedLength = LeaderNameFieldLength - 1;
    public const int SyndicateNameFieldLength = 17;
    public const int MaximumSyndicateNameEncodedLength = SyndicateNameFieldLength - 1;

    private const int StoredUnusedValueLength = sizeof(uint);

    public ushort PacketId => PacketIdentifier;
    public int PayloadLength => PayloadSize;
    public uint SyndicateId { get; } = syndicateId;
    public uint MemberProffer { get; } = memberProffer;
    public ulong SilverFund { get; } = silverFund;
    public uint EmoneyFund { get; } = emoneyFund;
    public uint Population { get; } = population;
    public uint MemberRank { get; } = memberRank;
    public string LeaderName { get; } = leaderName ?? throw new ArgumentNullException(nameof(leaderName));
    public uint RequiredLevel { get; } = requiredLevel;
    public uint RequiredMetempsychosis { get; } = requiredMetempsychosis;
    public uint RequiredProfession { get; } = requiredProfession;
    public byte SyndicateLevel { get; } = syndicateLevel;
    public ushort Mantle { get; } = mantle;
    public uint PositionExpirationDate { get; } = positionExpirationDate;
    public uint JoinDate { get; } = joinDate;
    public string SyndicateName { get; } = syndicateName ?? throw new ArgumentNullException(nameof(syndicateName));

    public void WritePayload(ref PacketWriter writer)
    {
        int start = writer.Written;

        writer.WriteUInt32(SyndicateId);
        writer.WriteUInt32(MemberProffer);
        writer.WriteUInt64(SilverFund);
        writer.WriteUInt32(EmoneyFund);
        writer.WriteUInt32(Population);
        writer.WriteUInt32(MemberRank);
        writer.WriteFixedString(LeaderName, MaximumLeaderNameEncodedLength, TqTextEncoding.StrictAnsi);
        writer.Reserve(LeaderNameFieldLength - MaximumLeaderNameEncodedLength);
        writer.WriteUInt32(RequiredLevel);
        writer.WriteUInt32(RequiredMetempsychosis);
        writer.WriteUInt32(RequiredProfession);
        writer.WriteByte(SyndicateLevel);
        writer.WriteUInt16(Mantle);
        writer.WriteUInt32(PositionExpirationDate);
        writer.WriteUInt32(JoinDate);

        // Native 5517 stores packet +71 but has no proven reader or business meaning.
        writer.Reserve(StoredUnusedValueLength);

        writer.WriteFixedString(SyndicateName, MaximumSyndicateNameEncodedLength, TqTextEncoding.StrictAnsi);
        writer.Reserve(SyndicateNameFieldLength - MaximumSyndicateNameEncodedLength);

        int payloadLength = writer.Written - start;
        if (payloadLength != PayloadSize)
        {
            throw new InvalidOperationException($"MsgSyndicateAttributeInfo payload must be exactly {PayloadSize} bytes; wrote {payloadLength}.");
        }
    }
}
