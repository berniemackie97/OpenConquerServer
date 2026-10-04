using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Skills.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

public sealed class CharacterWeaponSkillSetRepository(IDbContextFactory<GameDbContext> contextFactory, CharacterWeaponSkillHydrationOptions options) : ICharacterWeaponSkillSetRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly int _maximumSkillsPerCharacter = (options ?? throw new ArgumentNullException(nameof(options))).MaximumSkillsPerCharacter;

    public async ValueTask<CharacterWeaponSkillSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A weapon-skill lookup character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        List<WeaponSkillRecord> records = await db.WeaponSkills.AsNoTracking()
            .Where(skill => skill.OwnerCharacterId == characterId)
            .OrderBy(skill => skill.WeaponSkillType)
            .Take(_maximumSkillsPerCharacter + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (records.Count > _maximumSkillsPerCharacter)
        {
            throw new InvalidDataException($"Character {characterId} exceeds the configured weapon-skill hydration limit of {_maximumSkillsPerCharacter} skills.");
        }

        WeaponSkill[] skills = new WeaponSkill[records.Count];

        for (int index = 0; index < records.Count; index++)
        {
            WeaponSkillRecord record = records[index];

            try
            {
                skills[index] = WeaponSkill.Create(record.OwnerCharacterId, record.WeaponSkillType, record.Level, record.Experience);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Persisted weapon skill type {record.WeaponSkillType} for character {record.OwnerCharacterId} contains invalid hydrated state.", exception);
            }
        }

        try
        {
            return new CharacterWeaponSkillSet(characterId, skills);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted weapon-skill set for character {characterId} contains invalid aggregate state.", exception);
        }
    }
}
