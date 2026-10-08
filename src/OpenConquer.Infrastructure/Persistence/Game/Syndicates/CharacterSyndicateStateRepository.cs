using Microsoft.EntityFrameworkCore;
using OpenConquer.Application.Syndicates.Hydration;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Syndicates;
using OpenConquer.Infrastructure.Persistence.Game.Context;

namespace OpenConquer.Infrastructure.Persistence.Game.Syndicates;

public sealed class CharacterSyndicateStateRepository(IDbContextFactory<GameDbContext> contextFactory) : ICharacterSyndicateStateRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async ValueTask<CharacterSyndicateState> LoadAsync(uint characterId, CancellationToken cancellationToken = default)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), characterId, "Syndicate hydration requires a player character ID.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using GameDbContext db = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await (from membership in db.SyndicateMemberships.AsNoTracking()
                         where membership.CharacterId == characterId
                         join syndicate in db.Syndicates.AsNoTracking()
                             on membership.SyndicateId equals syndicate.SyndicateId into syndicates
                         from syndicate in syndicates.DefaultIfEmpty()
                         join leader in db.Characters.AsNoTracking()
                             on syndicate.LeaderCharacterId equals leader.CharacterId into leaders
                         from leader in leaders.DefaultIfEmpty()
                         select new
                         {
                             Membership = membership,
                             Syndicate = syndicate,
                             LeaderName = leader == null ? null : leader.Name,
                             Population = db.SyndicateMemberships.LongCount(candidate => candidate.SyndicateId == membership.SyndicateId)
                         })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (row is null)
        {
            return new CharacterSyndicateState(characterId, membership: null, syndicate: null);
        }

        if (row.Syndicate is null)
        {
            throw new InvalidDataException($"Syndicate membership for character {characterId} references a missing syndicate.");
        }

        if (row.LeaderName is null)
        {
            throw new InvalidDataException($"Syndicate {row.Syndicate.SyndicateId} references a missing leader character.");
        }

        if (row.Population < 1 || row.Population > uint.MaxValue)
        {
            throw new InvalidDataException($"Syndicate {row.Syndicate.SyndicateId} has an invalid population of {row.Population}.");
        }

        try
        {
            CharacterSyndicateMembership membership = CharacterSyndicateMembership.Create(row.Membership.CharacterId,
                row.Membership.SyndicateId, row.Membership.Rank, row.Membership.Proffer,
                row.Membership.PositionExpirationUnixSeconds, row.Membership.JoinDateUnixSeconds);

            Syndicate syndicate = new(row.Syndicate.SyndicateId, row.Syndicate.Name, row.Syndicate.LeaderCharacterId,
                row.LeaderName, row.Syndicate.SilverFund, row.Syndicate.EmoneyFund, checked((uint)row.Population),
                row.Syndicate.RequiredLevel, row.Syndicate.RequiredProfession, row.Syndicate.RequiredMetempsychosis);

            return new CharacterSyndicateState(characterId, membership, syndicate);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Persisted syndicate state for character {characterId} is invalid.", exception);
        }
    }
}
