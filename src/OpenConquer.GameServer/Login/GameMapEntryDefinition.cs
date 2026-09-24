namespace OpenConquer.GameServer.Login;

/// <summary>
/// Describes the verified client-facing metadata required to enter one GameServer map.
/// </summary>
internal sealed class GameMapEntryDefinition
{
    public GameMapEntryDefinition(uint mapId, uint mapDataId, ulong flags)
    {
        if (mapId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), "Map entry metadata requires a nonzero map ID.");
        }

        if (mapDataId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapDataId), "Map entry metadata requires a nonzero map data ID.");
        }

        MapId = mapId;
        MapDataId = mapDataId;
        Flags = flags;
    }

    public uint MapId { get; }
    public uint MapDataId { get; }
    public ulong Flags { get; }
}
