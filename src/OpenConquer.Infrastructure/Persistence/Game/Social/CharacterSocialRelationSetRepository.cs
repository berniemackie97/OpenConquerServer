using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Social.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Social;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Social;

public sealed class CharacterSocialRelationSetRepository(IDbContextFactory<GameDbContext> contextFactory, CharacterSocialRelationHydrationOptions options) : ICharacterSocialRelationSetRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly int _maximumRelationsPerCharacter = (options ?? throw new ArgumentNullException(nameof(options))).MaximumRelationsPerCharacter;

    public async ValueTask<CharacterSocialRelationSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A social-relation lookup character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (
            from relation in db.SocialRelations.AsNoTracking()
            join counterpart in db.Characters.AsNoTracking()
                on relation.CounterpartCharacterId equals counterpart.CharacterId into counterparts
            from counterpart in counterparts.DefaultIfEmpty()
            where relation.OwnerCharacterId == characterId
            orderby relation.Kind, relation.CounterpartCharacterId
            select new
            {
                Relation = relation,
                CounterpartName = counterpart == null ? null : counterpart.Name,
            })
            .Take(_maximumRelationsPerCharacter + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (rows.Count > _maximumRelationsPerCharacter)
        {
            throw new InvalidDataException($"Character {characterId} exceeds the configured social-relation hydration limit of {_maximumRelationsPerCharacter} relations.");
        }

        CharacterSocialRelation[] relations = new CharacterSocialRelation[rows.Count];

        for (int index = 0; index < rows.Count; index++)
        {
            SocialRelationRecord record = rows[index].Relation;
            string? counterpartName = rows[index].CounterpartName;

            if (counterpartName is null)
            {
                throw new InvalidDataException($"Persisted social relation from character {record.OwnerCharacterId} references missing counterpart character {record.CounterpartCharacterId}.");
            }

            try
            {
                SocialRelation relation = SocialRelation.Create(record.OwnerCharacterId, record.CounterpartCharacterId, record.Kind);
                relations[index] = new CharacterSocialRelation(relation, counterpartName);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Persisted social relation from character {record.OwnerCharacterId} to character {record.CounterpartCharacterId} contains invalid hydrated state.", exception);
            }
        }

        try
        {
            return new CharacterSocialRelationSet(characterId, relations);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted social-relation set for character {characterId} contains invalid aggregate state.", exception);
        }
    }
}
