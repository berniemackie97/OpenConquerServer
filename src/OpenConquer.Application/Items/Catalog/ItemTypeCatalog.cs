using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Catalog;

public sealed class ItemTypeCatalog
{
    private readonly FrozenDictionary<uint, ItemTypeDefinition> _definitions;

    public ItemTypeCatalog(IEnumerable<ItemTypeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        Dictionary<uint, ItemTypeDefinition> definitionsById = [];

        foreach (ItemTypeDefinition definition in definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("An item-type catalog cannot contain a null definition.", nameof(definitions));
            }

            if (!definitionsById.TryAdd(definition.ItemTypeId, definition))
            {
                throw new ArgumentException($"Item-type catalog contains duplicate item type {definition.ItemTypeId}.", nameof(definitions));
            }
        }

        if (definitionsById.Count == 0)
        {
            throw new ArgumentException("An item-type catalog cannot be empty.", nameof(definitions));
        }

        _definitions = definitionsById.ToFrozenDictionary();
    }

    public int Count => _definitions.Count;

    public bool TryGet(uint itemTypeId, [NotNullWhen(true)] out ItemTypeDefinition? definition)
    {
        return _definitions.TryGetValue(itemTypeId, out definition);
    }
}
