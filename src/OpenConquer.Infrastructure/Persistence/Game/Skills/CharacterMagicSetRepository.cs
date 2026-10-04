using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

public sealed class CharacterMagicSetRepository(IDbContextFactory<GameDbContext> contextFactory, CharacterMagicHydrationOptions options) : ICharacterMagicSetRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly int _maximumMagicEntriesPerCharacter = (options ?? throw new ArgumentNullException(nameof(options))).MaximumMagicEntriesPerCharacter;

    public async ValueTask<CharacterMagicSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A magic lookup character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<MagicRecord> records = await db.Magic.AsNoTracking()
            .Where(magic => magic.OwnerCharacterId == characterId)
            .OrderBy(magic => magic.MagicType)
            .Take(_maximumMagicEntriesPerCharacter + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (records.Count > _maximumMagicEntriesPerCharacter)
        {
            throw new InvalidDataException($"Character {characterId} exceeds the configured magic hydration limit of {_maximumMagicEntriesPerCharacter} entries.");
        }

        CharacterMagic[] magic = new CharacterMagic[records.Count];

        for (int index = 0; index < records.Count; index++)
        {
            MagicRecord record = records[index];

            try
            {
                magic[index] = CharacterMagic.Create(record.OwnerCharacterId, record.MagicType, record.Level, record.Experience);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Persisted magic type {record.MagicType} for character {record.OwnerCharacterId} contains invalid hydrated state.", exception);
            }
        }

        try
        {
            return new CharacterMagicSet(characterId, magic);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted magic set for character {characterId} contains invalid aggregate state.", exception);
        }
    }
}
