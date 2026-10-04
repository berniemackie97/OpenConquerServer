using OpenConquer.Domain.Characters;

namespace OpenConquer.Domain.Skills;

/// <summary>
/// Represents one magic entry owned by a character.
/// </summary>
public readonly record struct CharacterMagic
{
    private CharacterMagic(uint ownerCharacterId, ushort type, ushort level, uint experience)
    {
        OwnerCharacterId = ownerCharacterId;
        Type = type;
        Level = level;
        Experience = experience;
    }

    public uint OwnerCharacterId { get; }
    public ushort Type { get; }
    public ushort Level { get; }
    public uint Experience { get; }
    public bool IsValid => CharacterIdentityPolicy.IsPlayerEntityId(OwnerCharacterId);

    public static CharacterMagic Create(uint ownerCharacterId, ushort type, ushort level, uint experience)
    {
        if (!CharacterIdentityPolicy.IsPlayerEntityId(ownerCharacterId))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerCharacterId), ownerCharacterId, "Magic owner must identify a player character.");
        }

        return new CharacterMagic(ownerCharacterId, type, level, experience);
    }
}
