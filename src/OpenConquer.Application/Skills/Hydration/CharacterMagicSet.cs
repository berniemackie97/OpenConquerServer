using System.Collections.ObjectModel;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Skills;

namespace OpenConquer.Application.Skills.Hydration;

public sealed class CharacterMagicSet
{
    private readonly ReadOnlyCollection<CharacterMagic> _magic;

    public CharacterMagicSet(uint characterId, IEnumerable<CharacterMagic> magic)
    {
        ArgumentNullException.ThrowIfNull(magic);

        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"A magic set character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        CharacterMagic[] materializedMagic = magic.ToArray();
        HashSet<ushort> magicTypes = new(materializedMagic.Length);

        foreach (CharacterMagic entry in materializedMagic)
        {
            if (!entry.IsValid)
            {
                throw new ArgumentException("A magic set cannot contain an invalid magic entry.", nameof(magic));
            }

            if (entry.OwnerCharacterId != characterId)
            {
                throw new ArgumentException($"Magic type {entry.Type} belongs to character {entry.OwnerCharacterId}, not magic-set character {characterId}.", nameof(magic));
            }

            if (!magicTypes.Add(entry.Type))
            {
                throw new ArgumentException($"Magic set contains duplicate magic type {entry.Type}.", nameof(magic));
            }
        }

        Array.Sort(materializedMagic, static (left, right) => left.Type.CompareTo(right.Type));

        CharacterId = characterId;
        _magic = Array.AsReadOnly(materializedMagic);
    }

    public uint CharacterId { get; }
    public IReadOnlyList<CharacterMagic> Magic => _magic;
    public int Count => _magic.Count;
}
