using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Login.Profile;

public sealed class CharacterLoginIdentity
{
    public CharacterLoginIdentity(uint characterId, uint accountId, string name)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A persisted character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A persisted character must identify an account.");
        }

        if (!CharacterNamePolicy.IsValid(name))
        {
            throw new ArgumentException("A persisted character requires a valid character name.", nameof(name));
        }

        CharacterId = characterId;
        AccountId = accountId;
        Name = name;
    }

    public uint CharacterId { get; }
    public uint AccountId { get; }
    public string Name { get; }
}
