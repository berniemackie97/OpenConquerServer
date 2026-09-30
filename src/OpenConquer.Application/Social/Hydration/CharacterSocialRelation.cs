using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Application.Social.Hydration;

public sealed class CharacterSocialRelation
{
    public CharacterSocialRelation(SocialRelation relation, string counterpartName)
    {
        if (!relation.IsValid)
        {
            throw new ArgumentException("A hydrated social relation requires a valid relation.", nameof(relation));
        }

        if (!CharacterNamePolicy.IsValid(counterpartName))
        {
            throw new ArgumentException("A hydrated social relation requires a valid counterpart character name.", nameof(counterpartName));
        }

        Relation = relation;
        CounterpartName = counterpartName;
    }

    public SocialRelation Relation { get; }
    public string CounterpartName { get; }

    public uint OwnerCharacterId => Relation.OwnerCharacterId;
    public uint CounterpartCharacterId => Relation.CounterpartCharacterId;
    public SocialRelationKind Kind => Relation.Kind;
}
