namespace OpenConquer.Application.World.Transitions;

/// <summary>
/// Loads a world's map-transition data and checks it against the same map content the world runtime uses.
/// </summary>
public interface IMapTransitionCatalogRepository
{
    ValueTask<MapTransitionCatalog> LoadAsync(WorldMapContentCatalog maps, CancellationToken cancellationToken = default);
}
