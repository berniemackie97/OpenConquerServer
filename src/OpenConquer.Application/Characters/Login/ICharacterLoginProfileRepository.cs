namespace OpenConquer.Application.Characters.Login;

public interface ICharacterLoginProfileRepository
{
    ValueTask<CharacterLoginProfile?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default);
}
