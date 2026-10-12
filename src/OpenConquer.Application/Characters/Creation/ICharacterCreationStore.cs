namespace OpenConquer.Application.Characters.Creation;

public interface ICharacterCreationStore
{
    ValueTask<CharacterCreationStoreResult> CreateAsync(CharacterCreationCandidate character, CancellationToken cancellationToken = default);
}
