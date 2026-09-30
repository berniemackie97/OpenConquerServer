namespace OpenConquer.Application.Social.Hydration;

public interface ICharacterSocialRelationSetRepository
{
    ValueTask<CharacterSocialRelationSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default);
}
