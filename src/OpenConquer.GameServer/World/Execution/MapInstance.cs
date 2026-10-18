using System.Diagnostics;
using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;
using OpenConquer.Domain.World;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Character state for one map. Only touched during the map partition's turn.
/// </summary>
internal sealed class MapInstance(WorldMapContent map)
{
    private static readonly sbyte[] s_walkDeltaX = [0, -1, -1, -1, 0, 1, 1, 1];
    private static readonly sbyte[] s_walkDeltaY = [1, 1, 0, -1, -1, -1, 0, 1];

    // Selector-9 tables from CMsgEntityAction__Process in the 5517 client (0x86C804 and 0x86C864). They match
    // OpenConquerPublic's MsgEntityAction10005.
    private static readonly sbyte[] s_twoTileDeltaX =
        [0, -2, -2, -2, 0, 2, 2, 2, -1, -2, -2, -1, 1, 2, 2, 1, -1, -2, -2, -1, 1, 2, 2, 1];
    private static readonly sbyte[] s_twoTileDeltaY =
        [2, 2, 0, -2, -2, -2, 0, 2, 2, 1, -1, -2, -2, -1, 1, 2, 2, 1, -1, -2, -2, -1, 1, 2];
    private const int GroundJumpMaximumClimb = 200;
    private readonly Dictionary<uint, WorldCharacterSnapshot> _characters = [];
    // Map-partition-owned index. Counts allow ordinary walking to share tiles;
    // only ground-jump landing tests require a vacant living-player tile.
    private readonly Dictionary<(ushort X, ushort Y), int> _livingTileOccupancy = [];

    public WorldMapContent Map { get; } = map ?? throw new ArgumentNullException(nameof(map));

    public void Admit(WorldCharacterOwnership ownership, ushort x, ushort y, bool isAlive = true)
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

        if (!_characters.TryAdd(ownership.CharacterId, new WorldCharacterSnapshot(ownership, x, y, isAlive,
            WorldPrimaryStatusMask.ForAdmission(isAlive), isAlive ? 0 : Stopwatch.GetTimestamp())))
        {
            throw new InvalidOperationException($"Character {ownership.CharacterId} already belongs to map {Map.MapId}.");
        }

        if (isAlive)
        {
            try
            {
                AddLivingOccupant(x, y);
            }
            catch
            {
                _characters.Remove(ownership.CharacterId);
                throw;
            }
        }
    }

    public bool Remove(WorldCharacterOwnership ownership)
    {
        if (!_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) || current.Ownership != ownership)
        {
            return false;
        }

        if (!_characters.Remove(ownership.CharacterId))
        {
            throw new InvalidOperationException("The exclusive map partition lost an authenticated occupant.");
        }
        if (current.IsAlive)
        {
            RemoveLivingOccupant(current.X, current.Y);
        }
        return true;
    }

    public WorldCharacterSnapshot? Find(WorldCharacterOwnership ownership)
    {
        return _characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) && current.Ownership == ownership
            ? current
            : null;
    }

    /// <summary>
    /// Moves the character one step using the 5517 direction tables. The 24-entry table is used only for selector 9,
    /// which WorldRuntime refuses from the network until the server tracks riding and vigor. Runs during the
    /// partition's turn.
    /// </summary>
    public WorldCharacterSnapshot? TryWalk(WorldCharacterOwnership ownership, byte directionIndex, uint commandSelector = 0)
    {
        bool twoTile = commandSelector == 9;
        sbyte[] deltaX = twoTile ? s_twoTileDeltaX : s_walkDeltaX;
        sbyte[] deltaY = twoTile ? s_twoTileDeltaY : s_walkDeltaY;
        if (directionIndex >= deltaX.Length || !_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current)
            || current.Ownership != ownership || !current.CanWalk || (twoTile && !current.IsAlive))
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
        RelocateLivingOccupant(current, moved.X, moved.Y);
        _characters[ownership.CharacterId] = moved;
        return moved;
    }

    /// <summary>
    /// Turns the character into a ghost once the corpse delay has passed. Runs during the partition's turn.
    /// </summary>
    public WorldCharacterSnapshot? TryEnterGhost(WorldCharacterOwnership ownership, long serverTimestamp)
    {
        if (!_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) ||
            current.Ownership != ownership || current.IsAlive || current.IsGhost ||
            !current.Status.Has(WorldPrimaryStatusMask.Dead) || current.DeadSinceTimestamp == 0 ||
            serverTimestamp < current.DeadSinceTimestamp ||
            serverTimestamp - current.DeadSinceTimestamp < 3L * Stopwatch.Frequency)
        {
            return null;
        }

        WorldCharacterSnapshot ghost = current with { Status = current.Status.With(WorldPrimaryStatusMask.Ghost) };
        _characters[ownership.CharacterId] = ghost;
        return ghost;
    }

    // A two-tile move must not let a rider pass through a blocked cell in between. For (2,1) and (1,2) moves both
    // possible middle cells are checked. This is a cautious server rule until mounted collision is checked
    // against the client.
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

    /// <summary>
    /// Lands a jump on an absolute target tile. Runs during the partition's turn.
    /// </summary>
    public WorldCharacterSnapshot? TryJump(WorldCharacterOwnership ownership, ushort targetX, ushort targetY)
    {
        if (!_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current) ||
            current.Ownership != ownership || !current.IsAlive ||
            (Map.Flags & 0x0200_0000UL) != 0 ||
            !Map.Collision.IsStaticallyPassable(targetX, targetY))
        {
            return null;
        }

        int distance = MapDistance.RoundedTileDistance(current.X, current.Y, targetX, targetY);
        if (distance is < 1 or > 16 ||
            _livingTileOccupancy.ContainsKey((targetX, targetY)) ||
            !IsGroundJumpElevationAllowed(current.X, current.Y, targetX, targetY))
        {
            return null;
        }

        WorldCharacterSnapshot moved = current with { X = targetX, Y = targetY };
        RelocateLivingOccupant(current, targetX, targetY);
        _characters[ownership.CharacterId] = moved;
        return moved;
    }

    // Retail 5517 samples the rounded direct line, excluding the origin. Only
    // upward peak-minus-origin elevation is capped (200 for a non-flying role).
    // All samples use the composed collision terrain, not the unmodified DMap.
    private bool IsGroundJumpElevationAllowed(ushort fromX, ushort fromY, ushort toX, ushort toY)
    {
        int startElevation = Map.Collision.GetEffectiveCell(fromX, fromY).Elevation;
        int dx = (int)toX - fromX;
        int dy = (int)toY - fromY;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        for (int step = 1; step <= steps; step++)
        {
            int x = fromX + RoundedLineOffset(dx, step, steps);
            int y = fromY + RoundedLineOffset(dy, step, steps);
            if (Map.Collision.GetEffectiveCell(x, y).Elevation - startElevation > GroundJumpMaximumClimb)
            {
                return false;
            }
        }
        return true;
    }

    // Integer-only, nearest tile, midpoint away from the starting coordinate.
    // Jump admission limits both axes to at most 16, so products are bounded.
    private static int RoundedLineOffset(int delta, int step, int steps)
    {
        int rounded = ((Math.Abs(delta) * step * 2) + steps) / (steps * 2);
        return delta < 0 ? -rounded : rounded;
    }

    private void AddLivingOccupant(ushort x, ushort y)
    {
        (ushort X, ushort Y) tile = (x, y);
        _livingTileOccupancy[tile] = checked(_livingTileOccupancy.GetValueOrDefault(tile) + 1);
    }

    private void RemoveLivingOccupant(ushort x, ushort y)
    {
        (ushort X, ushort Y) tile = (x, y);
        int remaining = _livingTileOccupancy[tile] - 1;
        if (remaining == 0)
        {
            _livingTileOccupancy.Remove(tile);
        }
        else
        {
            _livingTileOccupancy[tile] = remaining;
        }
    }

    private void RelocateLivingOccupant(WorldCharacterSnapshot current, ushort x, ushort y)
    {
        if (current.IsAlive && (current.X != x || current.Y != y))
        {
            AddLivingOccupant(x, y);
            RemoveLivingOccupant(current.X, current.Y);
        }
    }

    public void Clear()
    {
        _characters.Clear();
        _livingTileOccupancy.Clear();
    }
}
