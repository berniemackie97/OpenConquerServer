namespace OpenConquer.Application.World;

public interface IMapBaseTerrainCatalogRepository
{
    ValueTask<MapBaseTerrainCatalog> LoadAsync(MapDefinitionCatalog definitions, CancellationToken cancellationToken = default);
}
