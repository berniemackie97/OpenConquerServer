namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Reads the persisted location and monotonic revision for an exact account/character identity.
/// The observation is not an ownership lease, commit receipt, or proof that a
/// previously uncertain location write has completed.
/// </summary>
public interface ICharacterLocationObservationStore
{
    ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default);
}
