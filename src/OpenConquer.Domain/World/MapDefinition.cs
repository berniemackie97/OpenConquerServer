namespace OpenConquer.Domain.World;

/// <summary>
/// Defines one server world-map identity, its client terrain identity, and its complete native map flags.
/// </summary>
public sealed record MapDefinition
{
    public MapDefinition(uint mapId, uint mapDataId, ulong flags)
    {
        if (mapId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), "World-map identity must be nonzero.");
        }

        if (mapDataId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapDataId), "Map-data identity must be nonzero.");
        }

        MapId = mapId;
        MapDataId = mapDataId;
        Flags = flags;
    }

    public uint MapId { get; }
    public uint MapDataId { get; }
    public ulong Flags { get; }
}
