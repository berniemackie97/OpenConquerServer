using OpenConquer.Domain.Items;

namespace OpenConquer.Application.Items.Catalog;

public interface IItemTypeCatalogRepository
{
    ValueTask<IReadOnlyList<ItemTypeDefinition>> LoadAllAsync(CancellationToken cancellationToken = default);
}
