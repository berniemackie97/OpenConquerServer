namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Probes the dedicated v13 location-writer identity before opening network ingress.
/// The implementation must require SELECT and UPDATE permissions without mutating character state.
/// </summary>
public interface ICharacterLocationWriterReadinessVerifier
{
    ValueTask VerifyPermissionsAsync(CancellationToken cancellationToken = default);
}
