namespace OpenConquer.Application.World;

public interface IMapDefinitionCatalogRepository
{
    ValueTask<MapDefinitionCatalog> LoadAsync(CancellationToken cancellationToken = default);
}
