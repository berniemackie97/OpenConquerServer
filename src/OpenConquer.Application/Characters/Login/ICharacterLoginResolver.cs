namespace OpenConquer.Application.Characters.Login;

/// <summary>
/// Resolves authenticated account identity to its persisted character-login route.
/// </summary>
public interface ICharacterLoginResolver
{
    /// <summary>
    /// Resolves <paramref name="accountId"/> to character creation or an existing
    /// persisted character.
    /// </summary>
    ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default);
}
