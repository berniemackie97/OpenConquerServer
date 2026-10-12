using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Creation;

/// <summary>
/// Validates a creation request, resolves trusted initial state, and invokes atomic persistence.
/// </summary>
public sealed class CharacterCreationService(ICharacterCreationStore store, CharacterCreationStartingState startingState)
{
    private readonly ICharacterCreationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly CharacterCreationStartingState _startingState = startingState ?? throw new ArgumentNullException(nameof(startingState));

    public async ValueTask<CharacterCreationResult> CreateAsync(uint accountId, string? name, ushort body, ushort profession, CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Character creation requires an authenticated account identity.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!CharacterNamePolicy.IsValid(name))
        {
            return CharacterCreationResult.InvalidName();
        }

        CharacterCreationCandidate? candidate = _startingState.CreateCandidate(accountId, name!, body, profession);

        if (candidate is null)
        {
            return CharacterCreationResult.InvalidSelection();
        }

        cancellationToken.ThrowIfCancellationRequested();

        CharacterCreationStoreResult persisted = await _store.CreateAsync(candidate, cancellationToken).ConfigureAwait(false);

        return persisted.Status switch
        {
            CharacterCreationStoreStatus.Created => CharacterCreationResult.Created(persisted.CharacterId),
            CharacterCreationStoreStatus.AccountAlreadyHasCharacter => CharacterCreationResult.AccountAlreadyHasCharacter(),
            CharacterCreationStoreStatus.NameAlreadyInUse => CharacterCreationResult.NameAlreadyInUse(),
            _ => throw new InvalidOperationException($"Character persistence returned unsupported status '{persisted.Status}'."),
        };
    }
}
