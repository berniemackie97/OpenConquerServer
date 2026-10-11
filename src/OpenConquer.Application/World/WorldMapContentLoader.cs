using OpenConquer.Domain.World;

namespace OpenConquer.Application.World;

public sealed class WorldMapContentLoader
{
    private readonly IMapDefinitionCatalogRepository _definitions;
    private readonly IMapBaseTerrainCatalogRepository _terrains;
    private readonly IMapTerrainCollisionCatalogRepository _collisions;

    public WorldMapContentLoader(IMapDefinitionCatalogRepository definitions, IMapBaseTerrainCatalogRepository terrains,
        IMapTerrainCollisionCatalogRepository collisions)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _terrains = terrains ?? throw new ArgumentNullException(nameof(terrains));
        _collisions = collisions ?? throw new ArgumentNullException(nameof(collisions));
    }

    public async ValueTask<WorldMapContentCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MapDefinitionCatalog definitions = await _definitions.LoadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        MapBaseTerrainCatalog terrains = await _terrains.LoadAsync(definitions, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        MapTerrainCollisionCatalog collisions = await _collisions.LoadAsync(definitions, terrains, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        int requiredTerrainCount = definitions.Definitions.Select(definition => definition.MapDataId).Distinct().Count();

        if (terrains.Count != requiredTerrainCount || collisions.Count != requiredTerrainCount)
        {
            throw new InvalidDataException($"World-map content requires {requiredTerrainCount} terrain identities, but received {terrains.Count} base terrains and {collisions.Count} collision entries.");
        }

        List<WorldMapContent> maps = new(definitions.Count);

        foreach (MapDefinition definition in definitions.Definitions.OrderBy(definition => definition.MapId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!terrains.TryGet(definition.MapDataId, out MapBaseTerrain? terrain))
            {
                throw new InvalidDataException($"World map {definition.MapId} references missing base terrain {definition.MapDataId}.");
            }

            if (!collisions.TryGet(definition.MapDataId, out MapTerrainCollision? collision))
            {
                throw new InvalidDataException($"World map {definition.MapId} references missing collision content {definition.MapDataId}.");
            }

            if (!ReferenceEquals(collision.BaseTerrain, terrain))
            {
                throw new InvalidDataException($"World map {definition.MapId} has collision content bound to a different base-terrain snapshot.");
            }

            maps.Add(new WorldMapContent(definition, collision));
        }

        return new WorldMapContentCatalog(maps);
    }
}
