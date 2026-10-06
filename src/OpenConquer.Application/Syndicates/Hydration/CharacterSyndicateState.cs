using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;

namespace OpenConquer.Application.Syndicates.Hydration;

public sealed class CharacterSyndicateState
{
    public CharacterSyndicateState(uint characterId, CharacterSyndicateMembership? membership, Syndicate? syndicate)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A syndicate-state character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        if (membership.HasValue != (syndicate is not null))
        {
            throw new ArgumentException("Syndicate membership and syndicate state must either both be present or both be absent.");
        }

        if (membership is { } persistedMembership)
        {
            if (!persistedMembership.IsValid)
            {
                throw new ArgumentException("Syndicate state cannot contain an invalid membership.", nameof(membership));
            }

            if (persistedMembership.CharacterId != characterId)
            {
                throw new ArgumentException($"Syndicate membership belongs to character {persistedMembership.CharacterId}, not syndicate-state character {characterId}.", nameof(membership));
            }

            if (persistedMembership.SyndicateId != syndicate!.SyndicateId)
            {
                throw new ArgumentException($"Syndicate membership references syndicate {persistedMembership.SyndicateId}, not hydrated syndicate {syndicate.SyndicateId}.", nameof(syndicate));
            }
        }

        CharacterId = characterId;
        Membership = membership;
        Syndicate = syndicate;
    }

    public uint CharacterId { get; }
    public CharacterSyndicateMembership? Membership { get; }
    public Syndicate? Syndicate { get; }
    public bool HasMembership => Membership.HasValue;
}
