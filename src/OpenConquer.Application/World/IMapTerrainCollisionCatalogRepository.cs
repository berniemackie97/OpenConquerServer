namespace OpenConquer.Application.World;

public interface IMapTerrainCollisionCatalogRepository
{
    ValueTask<MapTerrainCollisionCatalog> LoadAsync(MapDefinitionCatalog definitions, MapBaseTerrainCatalog terrains, CancellationToken cancellationToken = default);
}
