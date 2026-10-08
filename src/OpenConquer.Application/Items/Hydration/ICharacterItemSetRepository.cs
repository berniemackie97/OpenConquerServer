namespace OpenConquer.Application.Items.Hydration;

public interface ICharacterItemSetRepository
{
    ValueTask<CharacterItemSet> LoadAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}
