using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Creation;

public enum CharacterCreationStoreStatus
{
    Created = 0,
    AccountAlreadyHasCharacter,
    NameAlreadyInUse,
}

public readonly record struct CharacterCreationStoreResult
{
    private CharacterCreationStoreResult(CharacterCreationStoreStatus status, uint characterId)
    {
        Status = status;
        CharacterId = characterId;
    }

    public CharacterCreationStoreStatus Status { get; }
    public uint CharacterId { get; }

    public static CharacterCreationStoreResult Created(uint characterId)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }

        return new CharacterCreationStoreResult(CharacterCreationStoreStatus.Created, characterId);
    }

    public static CharacterCreationStoreResult AccountAlreadyHasCharacter()
    {
        return new CharacterCreationStoreResult(CharacterCreationStoreStatus.AccountAlreadyHasCharacter, 0);
    }

    public static CharacterCreationStoreResult NameAlreadyInUse()
    {
        return new CharacterCreationStoreResult(CharacterCreationStoreStatus.NameAlreadyInUse, 0);
    }
}
