using OpenConquer.Domain.Characters;

namespace OpenConquer.Domain.Syndicates;

/// <summary>
/// Represents the persisted syndicate-wide state required by the native 5517 syndicate bootstrap.
/// </summary>
public sealed class Syndicate
{
    public Syndicate(ushort syndicateId, string name, uint leaderCharacterId, string leaderName, ulong silverFund,
        uint emoneyFund, uint population, byte requiredLevel, byte requiredProfession, byte requiredMetempsychosis)
    {
        if (syndicateId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(syndicateId), syndicateId, "A syndicate requires a nonzero identity.");
        }

        ArgumentNullException.ThrowIfNull(name);

        if (!SyndicateNamePolicy.IsValid(name))
        {
            throw new ArgumentException("A syndicate requires a valid name.", nameof(name));
        }

        if (!CharacterIdentityPolicy.IsPlayerEntityId(leaderCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(leaderCharacterId), leaderCharacterId, "A syndicate leader must identify a player character.");
        }

        if (!CharacterNamePolicy.IsValid(leaderName))
        {
            throw new ArgumentException("A syndicate requires a valid leader character name.", nameof(leaderName));
        }

        SyndicateId = syndicateId;
        Name = name;
        LeaderCharacterId = leaderCharacterId;
        LeaderName = leaderName;
        SilverFund = silverFund;
        EmoneyFund = emoneyFund;
        Population = population;
        RequiredLevel = requiredLevel;
        RequiredProfession = requiredProfession;
        RequiredMetempsychosis = requiredMetempsychosis;
    }

    public ushort SyndicateId { get; }
    public string Name { get; }
    public uint LeaderCharacterId { get; }
    public string LeaderName { get; }
    public ulong SilverFund { get; }
    public uint EmoneyFund { get; }
    public uint Population { get; }
    public byte RequiredLevel { get; }
    public byte RequiredProfession { get; }
    public byte RequiredMetempsychosis { get; }
}
