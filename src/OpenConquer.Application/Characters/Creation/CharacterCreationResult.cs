using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Creation;

public enum CharacterCreationStatus
{
    Unspecified = 0,
    Created,
    InvalidName,
    InvalidSelection,
    AccountAlreadyHasCharacter,
    NameAlreadyInUse,
}

public readonly record struct CharacterCreationResult
{
    private CharacterCreationResult(CharacterCreationStatus status, uint characterId)
    {
        Status = status;
        CharacterId = characterId;
    }

    public CharacterCreationStatus Status { get; }
    public uint CharacterId { get; }

    public static CharacterCreationResult Created(uint characterId)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }

        return new CharacterCreationResult(CharacterCreationStatus.Created, characterId);
    }

    public static CharacterCreationResult InvalidName()
    {
        return new CharacterCreationResult(CharacterCreationStatus.InvalidName, 0);
    }

    public static CharacterCreationResult InvalidSelection()
    {
        return new CharacterCreationResult(CharacterCreationStatus.InvalidSelection, 0);
    }

    public static CharacterCreationResult AccountAlreadyHasCharacter()
    {
        return new CharacterCreationResult(CharacterCreationStatus.AccountAlreadyHasCharacter, 0);
    }

    public static CharacterCreationResult NameAlreadyInUse()
    {
        return new CharacterCreationResult(CharacterCreationStatus.NameAlreadyInUse, 0);
    }
}
