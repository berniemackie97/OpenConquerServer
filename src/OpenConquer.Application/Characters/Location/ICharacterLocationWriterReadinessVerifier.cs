namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Probes the GameServer identity's v13 location grants before opening network ingress.
/// The implementation must require SELECT and UPDATE permissions without mutating character state.
/// </summary>
public interface ICharacterLocationWriterReadinessVerifier
{
    ValueTask VerifyPermissionsAsync(CancellationToken cancellationToken = default);
}
