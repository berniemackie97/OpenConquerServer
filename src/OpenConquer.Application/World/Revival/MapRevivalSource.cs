namespace OpenConquer.Application.World.Revival;

/// <summary>
/// Historical server-map relationship and the map's own portal-zero coordinates.
/// The source map's portal is not its resurrection destination: the reborn target's portal is.
/// </summary>
public readonly record struct MapRevivalSource(
    uint MapId, uint RebornMapId, uint Portal0X, uint Portal0Y, string Provenance);
