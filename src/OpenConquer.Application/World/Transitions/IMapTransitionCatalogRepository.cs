namespace OpenConquer.Application.World.Transitions;

/// <summary>
/// Loads and validates world-specific map-transition evidence against the same immutable
/// map-content snapshot that will be used by the authoritative runtime.
/// </summary>
public interface IMapTransitionCatalogRepository
{
    ValueTask<MapTransitionCatalog> LoadAsync(WorldMapContentCatalog maps, CancellationToken cancellationToken = default);
}
