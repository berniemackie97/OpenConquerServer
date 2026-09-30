using OpenConquer.Domain.Characters;

namespace OpenConquer.Domain.Social;

public readonly record struct SocialRelation
{
    private SocialRelation(uint ownerCharacterId, uint counterpartCharacterId, SocialRelationKind kind)
    {
        OwnerCharacterId = ownerCharacterId;
        CounterpartCharacterId = counterpartCharacterId;
        Kind = kind;
    }

    public uint OwnerCharacterId { get; }
    public uint CounterpartCharacterId { get; }
    public SocialRelationKind Kind { get; }
    public bool IsValid => CharacterIdentityPolicy.IsPlayerEntityId(OwnerCharacterId)
        && CharacterIdentityPolicy.IsPlayerEntityId(CounterpartCharacterId)
        && OwnerCharacterId != CounterpartCharacterId
        && IsDefinedKind(Kind);

    public static SocialRelation Create(uint ownerCharacterId, uint counterpartCharacterId, SocialRelationKind kind)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(ownerCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerCharacterId), ownerCharacterId, "Social relation owner must identify a player character.");
        }

        if (!CharacterIdentityPolicy.IsPlayerEntityId(counterpartCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(counterpartCharacterId), counterpartCharacterId, "Social relation counterpart must identify a player character.");
        }

        if (ownerCharacterId == counterpartCharacterId)
        {
            throw new ArgumentException("A character cannot have a social relation with itself.", nameof(counterpartCharacterId));
        }

        if (!IsDefinedKind(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Social relation kind is not defined.");
        }

        return new SocialRelation(ownerCharacterId, counterpartCharacterId, kind);
    }

    private static bool IsDefinedKind(SocialRelationKind kind)
    {
        return kind is SocialRelationKind.Friend or SocialRelationKind.Enemy;
    }
}
