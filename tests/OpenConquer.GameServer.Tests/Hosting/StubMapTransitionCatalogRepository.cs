using OpenConquer.Application.World;
using OpenConquer.Application.World.Transitions;

namespace OpenConquer.GameServer.Tests.Hosting;

internal sealed class StubMapTransitionCatalogRepository(Exception? failure = null, bool useDifferentSnapshot = false) : IMapTransitionCatalogRepository
{
    public ValueTask<MapTransitionCatalog> LoadAsync(WorldMapContentCatalog maps, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            throw failure;
        }

        WorldMapContentCatalog validatedMaps = useDifferentSnapshot ? new WorldMapContentCatalog(maps.Maps) : maps;
        return ValueTask.FromResult(new MapTransitionCatalog([], validatedMaps));
    }
}
