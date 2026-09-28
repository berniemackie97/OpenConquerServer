namespace OpenConquer.Application.Items.Hydration;

public interface ICharacterItemSetRepository
{
    ValueTask<CharacterItemSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default);
}
