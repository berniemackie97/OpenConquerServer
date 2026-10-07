using OpenConquer.Application.Items.Hydration;

namespace OpenConquer.Application.Items.Resolution;

public interface ICharacterItemSetResolver
{
    ValueTask<CharacterItemSet> ResolveAsync(uint characterId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}
