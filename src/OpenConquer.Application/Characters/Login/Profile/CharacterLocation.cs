namespace OpenConquer.Application.Characters.Login.Profile;

public sealed class CharacterLocation
{
    public CharacterLocation(uint mapId, ushort x, ushort y)
    {
        if (mapId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), "A persisted character requires a nonzero map ID.");
        }

        MapId = mapId;
        X = x;
        Y = y;
    }

    public uint MapId { get; }
    public ushort X { get; }
    public ushort Y { get; }
}
