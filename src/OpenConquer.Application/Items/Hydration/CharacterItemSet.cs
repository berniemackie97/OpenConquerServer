using System.Collections.ObjectModel;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Hydration;

public sealed class CharacterItemSet
{
    private readonly ReadOnlyCollection<CharacterItem> _items;

    public CharacterItemSet(uint characterId, IEnumerable<CharacterItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), $"An item set character ID must be at least {CharacterIdentityPolicy.FirstPlayerEntityId}.");
        }

        CharacterItem[] materializedItems = items.ToArray();
        HashSet<uint> itemIds = new(materializedItems.Length);
        HashSet<EquipmentPosition> equipmentPositions = [];

        foreach (CharacterItem item in materializedItems)
        {
            if (item is null)
            {
                throw new ArgumentException("An item set cannot contain a null item.", nameof(items));
            }

            if (item.OwnerCharacterId != characterId)
            {
                throw new ArgumentException($"Item {item.ItemId} belongs to character {item.OwnerCharacterId}, not item-set character {characterId}.", nameof(items));
            }

            if (!itemIds.Add(item.ItemId))
            {
                throw new ArgumentException($"Item set contains duplicate item ID {item.ItemId}.", nameof(items));
            }

            if (item.Placement.EquipmentPosition is { } equipmentPosition && !equipmentPositions.Add(equipmentPosition))
            {
                throw new ArgumentException($"Item set contains duplicate equipment position {equipmentPosition.Set}/{equipmentPosition.Slot}.", nameof(items));
            }
        }

        Array.Sort(materializedItems, static (left, right) => left.ItemId.CompareTo(right.ItemId));

        CharacterId = characterId;
        _items = Array.AsReadOnly(materializedItems);
    }

    public uint CharacterId { get; }
    public IReadOnlyList<CharacterItem> Items => _items;
    public int Count => _items.Count;
}
