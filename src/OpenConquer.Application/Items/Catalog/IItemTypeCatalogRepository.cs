namespace OpenConquer.Application.Items.Catalog;

public interface IItemTypeCatalogRepository
{
    ValueTask<ItemTypeCatalog> LoadAsync(CancellationToken cancellationToken = default);
}
