namespace OpenConquer.Infrastructure.Persistence.Game.Syndicates;

internal sealed class SyndicateMembershipRecord
{
    public uint CharacterId { get; set; }
    public ushort SyndicateId { get; set; }
    public uint Rank { get; set; }
    public uint Proffer { get; set; }
    public uint PositionExpirationUnixSeconds { get; set; }
    public uint JoinDateUnixSeconds { get; set; }
}
