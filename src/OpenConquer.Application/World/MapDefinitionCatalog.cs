using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class MapDefinitionCatalog
{
    private readonly FrozenDictionary<uint, MapDefinition> _definitions;

    public MapDefinitionCatalog(IEnumerable<MapDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        Dictionary<uint, MapDefinition> definitionsById = [];

        foreach (MapDefinition definition in definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("A map-definition catalog cannot contain a null definition.", nameof(definitions));
            }

            if (!definitionsById.TryAdd(definition.MapId, definition))
            {
                throw new ArgumentException($"Map-definition catalog contains duplicate world-map ID {definition.MapId}.", nameof(definitions));
            }
        }

        if (definitionsById.Count == 0)
        {
            throw new ArgumentException("A map-definition catalog cannot be empty.", nameof(definitions));
        }

        _definitions = definitionsById.ToFrozenDictionary();
    }

    public int Count => _definitions.Count;

    public bool TryGet(uint mapId, [NotNullWhen(true)] out MapDefinition? definition)
    {
        return _definitions.TryGetValue(mapId, out definition);
    }
}
