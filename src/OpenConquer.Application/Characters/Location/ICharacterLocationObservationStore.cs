namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Reads the persisted location and revision for one account and character. It is only a read: it locks
/// nothing and does not prove that an earlier write with an unknown outcome has finished.
/// </summary>
public interface ICharacterLocationObservationStore
{
    ValueTask<PersistedCharacterLocation?> ObserveAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default);
}
