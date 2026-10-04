namespace OpenConquer.Application.Skills.Hydration;

public interface ICharacterWeaponSkillSetRepository
{
    ValueTask<CharacterWeaponSkillSet> LoadAsync(uint characterId, CancellationToken cancellationToken = default);
}
