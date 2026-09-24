using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Characters.Login;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Login;

public sealed class CharacterLoginProfileRepository(IDbContextFactory<GameDbContext> contextFactory) : ICharacterLoginProfileRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A character lookup requires a nonzero account ID.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        CharacterRecord? character = await db.Characters.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.AccountId == accountId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        return character is null ? null : CreateLoginProfile(character);
    }

    private static CharacterLoginProfile CreateLoginProfile(CharacterRecord character)
    {
        CharacterLoginIdentity identity = new(character.CharacterId, character.AccountId, character.Name);
        CharacterAppearance appearance = new(character.AppearanceComposite, character.HairComposite);
        CharacterProgression progression = new(character.Level, character.Experience, character.Profession, character.FirstProfession, character.PreviousProfession, character.RebirthCount, character.PreRebirthLevel);
        CharacterAttributes attributes = new(character.Strength, character.Agility, character.Vitality, character.Spirit, character.UnspentAttributePoints);
        CharacterVitals vitals = new(character.CurrentLife, character.CurrentMana);
        CharacterEconomy economy = new(character.Silver, character.ConquerPoints, character.BoundConquerPoints);
        CharacterLocation location = new(character.MapId, character.PositionX, character.PositionY);

        return new CharacterLoginProfile(identity, appearance, progression, attributes, vitals, economy, character.PkPoints, character.TitleId, character.EnlightenmentPoints, location);
    }
}
