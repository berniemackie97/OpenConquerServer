namespace OpenConquer.Application.Skills.Hydration;

public interface ICharacterMagicSetRepository
{
    ValueTask<CharacterMagicSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default);
}
