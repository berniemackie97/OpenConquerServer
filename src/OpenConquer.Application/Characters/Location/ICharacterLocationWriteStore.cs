namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Compare-and-swap of a character's persisted location, keyed on its revision. A conflict doesn't say why
/// (row deleted, owner changed or another write landed first). An unexpected failure may or may not have
/// committed, so don't retry it blindly.
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
