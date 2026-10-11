using OpenConquer.Application.Items.Catalog;

namespace OpenConquer.GameServer.Hosting;

internal sealed class GameServerItemCatalogState
{
    private ItemTypeCatalog? _catalog;

    public ItemTypeCatalog GetRequiredCatalog()
    {
        return Volatile.Read(ref _catalog) ?? throw new InvalidOperationException("The canonical GameServer item-type catalog is not ready.");
    }

    public void Publish(ItemTypeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (Interlocked.CompareExchange(ref _catalog, catalog, null) is not null)
        {
            throw new InvalidOperationException("The canonical GameServer item-type catalog has already been published.");
        }
    }
}
