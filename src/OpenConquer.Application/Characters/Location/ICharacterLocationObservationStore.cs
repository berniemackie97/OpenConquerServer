using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Reads only the persisted location for an exact account/character identity.
/// The observation is not an ownership lease, commit receipt, or proof that a
/// previously uncertain location write has completed.
/// </summary>
public interface ICharacterLocationObservationStore
{
    ValueTask<CharacterLocation?> ObserveAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default);
}
