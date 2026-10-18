using System.Collections.Frozen;
using OpenConquer.Application.World;

namespace OpenConquer.Application.World.Revival;

/// <summary>
/// Looks up where a character revives on each map, from the historical reborn-map data checked against the
/// 5517 terrain. There is no fallback to the nearest town or a portal chain.
/// </summary>
public sealed class RevivalDestinationCatalog
{
    private readonly FrozenDictionary<uint, RevivalDestinationResolution> _resolutions;

    public RevivalDestinationCatalog(WorldMapContentCatalog maps, IEnumerable<MapRevivalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(sources);

        Dictionary<uint, MapRevivalSource> rows = [];
        foreach (MapRevivalSource source in sources)
        {
            if (source.MapId == 0 || string.IsNullOrWhiteSpace(source.Provenance))
            {
                throw new ArgumentException("Revival evidence requires a nonzero world ID and provenance.", nameof(sources));
            }
            if (!maps.TryGet(source.MapId, out _))
            {
                throw new ArgumentException($"Revival evidence references unavailable source map {source.MapId}.", nameof(sources));
            }
            if (!rows.TryAdd(source.MapId, source))
            {
                throw new ArgumentException($"Duplicate revival evidence for map {source.MapId}.", nameof(sources));
            }
        }

        Dictionary<uint, RevivalDestinationResolution> results = new(maps.Count);
        foreach (WorldMapContent map in maps.Maps)
        {
            results.Add(map.MapId, Resolve(map.MapId, maps, rows));
        }

        _resolutions = results.ToFrozenDictionary();
        SourceRecordCount = rows.Count;
        ResolvedCount = results.Values.Count(static result => result.IsResolved);
    }

    public int SourceRecordCount { get; }
    public int ResolvedCount { get; }
    public int MapCount => _resolutions.Count;

    /// <summary>
    /// Returns the destination, or the reason there is none. A failed result never carries a coordinate, so a
    /// caller can't use one by mistake.
    /// </summary>
    public RevivalDestinationResolution Resolve(uint worldMapId) =>
        _resolutions.TryGetValue(worldMapId, out RevivalDestinationResolution result)
            ? result
            : new RevivalDestinationResolution(RevivalDestinationStatus.UnavailableWorldMap);

    private static RevivalDestinationResolution Resolve(uint sourceId, WorldMapContentCatalog maps,
        IReadOnlyDictionary<uint, MapRevivalSource> rows)
    {
        if (!rows.TryGetValue(sourceId, out MapRevivalSource source))
        {
            return new(RevivalDestinationStatus.MissingSourceEvidence);
        }
        if (source.RebornMapId == 0)
        {
            return new(RevivalDestinationStatus.MissingRebornMap);
        }
        if (!maps.TryGet(source.RebornMapId, out WorldMapContent? destinationMap))
        {
            return new(RevivalDestinationStatus.UnavailableTargetMap);
        }
        if (!rows.TryGetValue(source.RebornMapId, out MapRevivalSource destination))
        {
            return new(RevivalDestinationStatus.MissingTargetEvidence);
        }
        if (destination.Portal0X == 0 || destination.Portal0Y == 0)
        {
            return new(RevivalDestinationStatus.MissingTargetPortal);
        }
        if (destination.Portal0X > ushort.MaxValue || destination.Portal0Y > ushort.MaxValue ||
            destination.Portal0X >= destinationMap.Collision.Width ||
            destination.Portal0Y >= destinationMap.Collision.Height ||
            !destinationMap.Collision.IsStaticallyPassable((int)destination.Portal0X, (int)destination.Portal0Y))
        {
            return new(RevivalDestinationStatus.InvalidTargetPortal);
        }

        return new(RevivalDestinationStatus.Resolved, source.RebornMapId,
            checked((ushort)destination.Portal0X), checked((ushort)destination.Portal0Y));
    }
}
