using OpenConquer.Domain.Characters;

namespace OpenConquer.Domain.Syndicates;

/// <summary>
/// Represents one character's persisted membership in a syndicate.
/// </summary>
public readonly record struct CharacterSyndicateMembership
{
    private CharacterSyndicateMembership(uint characterId, ushort syndicateId, uint rank, long proffer,
        uint positionExpirationUnixSeconds, uint joinDateUnixSeconds)
    {
        CharacterId = characterId;
        SyndicateId = syndicateId;
        Rank = rank;
        Proffer = proffer;
        PositionExpirationUnixSeconds = positionExpirationUnixSeconds;
        JoinDateUnixSeconds = joinDateUnixSeconds;
    }

    public uint CharacterId { get; }
    public ushort SyndicateId { get; }
    public uint Rank { get; }
    public long Proffer { get; }
    public uint PositionExpirationUnixSeconds { get; }
    public uint JoinDateUnixSeconds { get; }

    public bool IsValid => CharacterIdentityPolicy.IsPlayerEntityId(CharacterId) && SyndicateId != 0;

    public static CharacterSyndicateMembership Create(uint characterId, ushort syndicateId, uint rank, long proffer,
        uint positionExpirationUnixSeconds, uint joinDateUnixSeconds)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), characterId, "A syndicate membership owner must identify a player character.");
        }

        if (syndicateId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(syndicateId), syndicateId, "A syndicate membership requires a nonzero syndicate identity.");
        }

        return new CharacterSyndicateMembership(characterId, syndicateId, rank, proffer, positionExpirationUnixSeconds, joinDateUnixSeconds);
    }
}
