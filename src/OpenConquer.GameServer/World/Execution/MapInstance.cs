using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Owns mutable map-local character state. Access is restricted to exclusive partition execution turns.
/// </summary>
internal sealed class MapInstance(WorldMapContent map)
{
    private static readonly sbyte[] s_walkDeltaX = [0, -1, -1, -1, 0, 1, 1, 1];
    private static readonly sbyte[] s_walkDeltaY = [1, 1, 0, -1, -1, -1, 0, 1];

    // CMsgEntityAction__Process selector-9 tables at native 5517 addresses
    // 0x86C804 / 0x86C864, corroborated by OpenConquerPublic's MsgEntityAction10005.
    private static readonly sbyte[] s_twoTileDeltaX =
        [0, -2, -2, -2, 0, 2, 2, 2, -1, -2, -2, -1, 1, 2, 2, 1, -1, -2, -2, -1, 1, 2, 2, 1];
    private static readonly sbyte[] s_twoTileDeltaY =
        [2, 2, 0, -2, -2, -2, 0, 2, 2, 1, -1, -2, -2, -1, 1, 2, 2, 1, -1, -2, -2, -1, 1, 2];
    private readonly Dictionary<uint, WorldCharacterSnapshot> _characters = [];

    public WorldMapContent Map { get; } = map ?? throw new ArgumentNullException(nameof(map));

    public void Admit(WorldCharacterOwnership ownership, ushort x, ushort y)
    {
        if (ownership.MapId != Map.MapId)
        {
            throw new ArgumentException("Character ownership belongs to a different map.", nameof(ownership));
        }

        if (!CharacterIdentityPolicy.IsPlayerEntityId(ownership.CharacterId) || ownership.Generation == 0)
        {
            throw new ArgumentException("Character ownership requires a valid player ID and nonzero generation.", nameof(ownership));
        }

        if (x >= Map.Collision.Width || y >= Map.Collision.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Character position is outside the canonical map terrain.");
        }

        if (!_characters.TryAdd(ownership.CharacterId, new WorldCharacterSnapshot(ownership, x, y)))
        {
            throw new InvalidOperationException($"Character {ownership.CharacterId} already belongs to map {Map.MapId}.");
        }
    }

    public bool Remove(WorldCharacterOwnership ownership)
    {
        if (!_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) || current.Ownership != ownership)
        {
            return false;
        }

        return _characters.Remove(ownership.CharacterId);
    }

    public WorldCharacterSnapshot? Find(WorldCharacterOwnership ownership)
    {
        return _characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) && current.Ownership == ownership
            ? current
            : null;
    }

    /// <summary>
    /// Applies one verified native 5517 direction-table delta under exclusive partition
    /// ownership. The full selector selects the 24-entry table only when exactly nine.
    /// </summary>
    public WorldCharacterSnapshot? TryWalk(WorldCharacterOwnership ownership, byte directionIndex, uint commandSelector = 0)
    {
        bool twoTile = commandSelector == 9;
        sbyte[] deltaX = twoTile ? s_twoTileDeltaX : s_walkDeltaX;
        sbyte[] deltaY = twoTile ? s_twoTileDeltaY : s_walkDeltaY;
        if (directionIndex >= deltaX.Length || !_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current)
            || current.Ownership != ownership)
        {
            return null;
        }

        int x = current.X + deltaX[directionIndex];
        int y = current.Y + deltaY[directionIndex];

        if ((uint)x > ushort.MaxValue || (uint)y > ushort.MaxValue || !Map.Collision.IsStaticallyPassable(x, y) ||
            (twoTile && !HasClearTwoTilePath(current, deltaX[directionIndex], deltaY[directionIndex])))
        {
            return null;
        }

        WorldCharacterSnapshot moved = current with { X = (ushort)x, Y = (ushort)y };
        _characters[ownership.CharacterId] = moved;
        return moved;
    }

    // A two-tile selector must not let an unauthenticated rider traverse a
    // statically blocked intervening cell. For asymmetric (2,1)/(1,2) motion
    // both plausible interior cells are checked conservatively. This is a
    // defensive server rule pending native mounted collision verification.
    private bool HasClearTwoTilePath(WorldCharacterSnapshot current, int dx, int dy)
    {
        int sx = Math.Sign(dx);
        int sy = Math.Sign(dy);
        if (Math.Abs(dx) == 2)
        {
            return Map.Collision.IsStaticallyPassable(current.X + sx, current.Y) &&
                (dy == 0 || Map.Collision.IsStaticallyPassable(current.X + sx, current.Y + sy));
        }

        return Map.Collision.IsStaticallyPassable(current.X, current.Y + sy) &&
            (dx == 0 || Map.Collision.IsStaticallyPassable(current.X + sx, current.Y + sy));
    }

    /// <summary>Commits a native absolute-target jump on this map's exclusive partition turn.</summary>
    public WorldCharacterSnapshot? TryJump(WorldCharacterOwnership ownership, ushort targetX, ushort targetY)
    {
        if (!_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) ||
            current.Ownership != ownership || (Map.Flags & 0x0200_0000UL) != 0 ||
            !Map.Collision.IsStaticallyPassable(targetX, targetY))
        {
            return null;
        }

        int distance = MapDistance.RoundedTileDistance(current.X, current.Y, targetX, targetY);
        if (distance is < 1 or > 16)
        {
            return null;
        }

        WorldCharacterSnapshot moved = current with { X = targetX, Y = targetY };
        _characters[ownership.CharacterId] = moved;
        return moved;
    }

    public void Clear() => _characters.Clear();
}
