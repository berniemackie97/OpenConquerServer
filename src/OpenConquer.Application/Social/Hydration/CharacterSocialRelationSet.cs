using System.Collections.ObjectModel;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;

namespace OpenConquer.Application.Social.Hydration;

public sealed class CharacterSocialRelationSet
{
    private readonly ReadOnlyCollection<CharacterSocialRelation> _relations;

    public CharacterSocialRelationSet(uint characterId, IEnumerable<CharacterSocialRelation> relations)
    {
        ArgumentNullException.ThrowIfNull(relations);

        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A social-relation set character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        CharacterSocialRelation[] materializedRelations = relations.ToArray();
        HashSet<(SocialRelationKind Kind, uint CounterpartCharacterId)> relationKeys = new(materializedRelations.Length);

        foreach (CharacterSocialRelation relation in materializedRelations)
        {
            if (relation is null)
            {
                throw new ArgumentException("A social-relation set cannot contain a null relation.", nameof(relations));
            }

            if (relation.OwnerCharacterId != characterId)
            {
                throw new ArgumentException(
                    $"Social relation counterpart {relation.CounterpartCharacterId} belongs to owner {relation.OwnerCharacterId}, not relation-set character {characterId}.",
                    nameof(relations));
            }

            if (!relationKeys.Add((relation.Kind, relation.CounterpartCharacterId)))
            {
                throw new ArgumentException(
                    $"Social-relation set contains duplicate {relation.Kind} relation to character {relation.CounterpartCharacterId}.",
                    nameof(relations));
            }
        }

        Array.Sort(materializedRelations, static (left, right) =>
        {
            int kindComparison = left.Kind.CompareTo(right.Kind);
            return kindComparison != 0 ? kindComparison : left.CounterpartCharacterId.CompareTo(right.CounterpartCharacterId);
        });

        CharacterId = characterId;
        _relations = Array.AsReadOnly(materializedRelations);
    }

    public uint CharacterId { get; }
    public IReadOnlyList<CharacterSocialRelation> Relations => _relations;
    public int Count => _relations.Count;
}
