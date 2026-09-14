namespace OpenConquer.Application.Characters.Login;

public sealed class CharacterLoginResolver(ICharacterLoginProfileRepository repository) : ICharacterLoginResolver
{
    private readonly ICharacterLoginProfileRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<CharacterLoginResolution> ResolveAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Character login resolution requires a nonzero account ID.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        CharacterLoginProfile? profile = await _repository.FindByAccountIdAsync(accountId, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (profile is null)
        {
            return CharacterLoginResolution.CharacterCreation();
        }

        if (profile.Identity.AccountId != accountId)
        {
            throw new InvalidOperationException("Character persistence returned a profile belonging to a different account.");
        }

        return CharacterLoginResolution.ExistingCharacter(profile);
    }
}
