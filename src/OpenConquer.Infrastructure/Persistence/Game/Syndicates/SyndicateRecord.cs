namespace OpenConquer.Infrastructure.Persistence.Game.Syndicates;

internal sealed class SyndicateRecord
{
    public ushort SyndicateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint LeaderCharacterId { get; set; }
    public ulong SilverFund { get; set; }
    public uint EmoneyFund { get; set; }
    public byte RequiredLevel { get; set; }
    public byte RequiredProfession { get; set; }
    public byte RequiredMetempsychosis { get; set; }
}
