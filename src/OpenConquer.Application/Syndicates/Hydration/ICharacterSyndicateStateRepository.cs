namespace OpenConquer.Application.Syndicates.Hydration;

public interface ICharacterSyndicateStateRepository
{
    ValueTask<CharacterSyndicateState> LoadAsync(uint characterId, CancellationToken cancellationToken = default);
}
