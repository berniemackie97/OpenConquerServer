namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Revision-qualified persistent location compare-and-swap. A conflict does not imply whether the row was
/// deleted, ownership changed, or another writer committed first. Unexpected failures
/// have uncertain commit outcomes and MUST NOT be retried blindly.
/// </summary>
public interface ICharacterLocationWriteStore
{
    ValueTask<CharacterLocationWriteResult> TryCommitAsync(CharacterLocationChange change,
        CancellationToken cancellationToken = default);
}

public enum CharacterLocationWriteResult
{
    Conflict,
    Committed,
}
