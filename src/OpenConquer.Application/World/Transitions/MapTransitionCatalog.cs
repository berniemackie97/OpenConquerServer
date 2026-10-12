using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OpenConquer.Domain.World;

namespace OpenConquer.Application.World.Transitions;

public sealed record MapTransitionDestination(uint MapId, uint PortalIndex, int X, int Y, string EvidenceSource);

public sealed record MapTransitionEntry(uint MapId, uint PasswayIndex, string Status, MapTransitionDestination? Destination);

public sealed class MapTransitionCatalog
{
    private readonly FrozenDictionary<(uint MapId, uint PasswayIndex), MapTransitionDestination> _destinations;

    public MapTransitionCatalog(IEnumerable<MapTransitionEntry> entries, WorldMapContentCatalog maps)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(maps);

        HashSet<(uint MapId, uint PasswayIndex)> expected = [];
        foreach (WorldMapContent map in maps.Maps)
        {
            foreach (MapTerrainExit exit in map.BaseTerrain.Exits)
            {
                expected.Add((map.MapId, exit.PasswayIndex));
            }
        }

        HashSet<(uint MapId, uint PasswayIndex)> seen = [];
        Dictionary<(uint MapId, uint PasswayIndex), MapTransitionDestination> destinations = [];

        foreach (MapTransitionEntry entry in entries)
        {
            if (entry is null)
            {
                throw new InvalidDataException("Map-transition catalog contains a null entry.");
            }

            (uint MapId, uint PasswayIndex) key = (entry.MapId, entry.PasswayIndex);
            if (!expected.Contains(key))
            {
                throw new InvalidDataException($"Map transition ({entry.MapId},{entry.PasswayIndex}) does not exist in the canonical native terrain exits.");
            }

            if (!seen.Add(key))
            {
                throw new InvalidDataException($"Duplicate map transition ({entry.MapId},{entry.PasswayIndex}).");
            }

            if (entry.Status == "resolved")
            {
                MapTransitionDestination destination = entry.Destination
                    ?? throw new InvalidDataException($"Resolved map transition ({entry.MapId},{entry.PasswayIndex}) lacks its destination.");

                if (destination.EvidenceSource is not ("L" or "T7" or "Q"))
                {
                    throw new InvalidDataException($"Map transition ({entry.MapId},{entry.PasswayIndex}) has an unknown historical source.");
                }

                if (!maps.TryGet(destination.MapId, out WorldMapContent? target))
                {
                    throw new InvalidDataException($"Map transition ({entry.MapId},{entry.PasswayIndex}) points to unavailable map {destination.MapId}.");
                }

                if (!target.Collision.IsStaticallyPassable(destination.X, destination.Y))
                {
                    throw new InvalidDataException($"Map transition ({entry.MapId},{entry.PasswayIndex}) points to a blocked or out-of-bounds arrival tile.");
                }

                destinations.Add(key, destination);
            }
            else if (entry.Status is "no-source" or "shared-terrain-unresolved" or "blocked-destination")
            {
                if (entry.Destination is not null)
                {
                    throw new InvalidDataException($"Unresolved map transition ({entry.MapId},{entry.PasswayIndex}) must not expose a destination.");
                }
            }
            else
            {
                throw new InvalidDataException($"Map transition ({entry.MapId},{entry.PasswayIndex}) has an unknown status.");
            }
        }

        if (seen.Count != expected.Count)
        {
            (uint MapId, uint PasswayIndex) missing = expected.Except(seen).First();
            throw new InvalidDataException($"Map-transition catalog omits native exit ({missing.MapId},{missing.PasswayIndex}).");
        }

        Count = seen.Count;
        ResolvedCount = destinations.Count;
        _destinations = destinations.ToFrozenDictionary();
    }

    public int Count { get; }
    public int ResolvedCount { get; }
    public int UnresolvedCount => Count - ResolvedCount;

    // PasswayIndex must come from the server's own DMap exit-marker lookup, never from a client-provided index.
    public bool TryResolve(uint mapId, uint passwayIndex, [NotNullWhen(true)] out MapTransitionDestination? destination)
    {
        return _destinations.TryGetValue((mapId, passwayIndex), out destination);
    }
}
