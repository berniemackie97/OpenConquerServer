namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Rotates a character's durable write generation using an atomic row update. The returned
/// position is observed after rotation; an older generation can no longer win a CAS.
/// A null result means the identity is absent or its generation changed before observation.
/// This does not confer world/session ownership.
/// </summary>
public interface ICharacterLocationFenceStore
{
    ValueTask<CharacterLocationWriteAuthority?> AcquireAsync(uint accountId, uint characterId,
        CancellationToken cancellationToken = default);
}
