namespace OpenConquer.Application.Characters.Login.Profile;

public interface ICharacterLoginProfileRepository
{
    ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default);
}
