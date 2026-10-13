using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Owns mutable map-local character state. Access is restricted to exclusive partition execution turns.
/// </summary>
internal sealed class MapInstance(WorldMapContent map)
{
    private static readonly sbyte[] s_walkDeltaX = [0, -1, -1, -1, 0, 1, 1, 1];
    private static readonly sbyte[] s_walkDeltaY = [1, 1, 0, -1, -1, -1, 0, 1];
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
    /// Applies one 5517 eight-direction ground step. This is a partition-owned primitive, not an ingress authorization policy.
    /// </summary>
    public WorldCharacterSnapshot? TryWalk(WorldCharacterOwnership ownership, byte directionIndex)
    {
        if (directionIndex >= s_walkDeltaX.Length || !_characters.TryGetValue(ownership.CharacterId, out WorldCharacterSnapshot current)
            || current.Ownership != ownership)
        {
            return null;
        }

        int x = current.X + s_walkDeltaX[directionIndex];
        int y = current.Y + s_walkDeltaY[directionIndex];

        if ((uint)x > ushort.MaxValue || (uint)y > ushort.MaxValue || !Map.Collision.IsStaticallyPassable(x, y))
        {
            return null;
        }

        WorldCharacterSnapshot moved = current with { X = (ushort)x, Y = (ushort)y };
        _characters[ownership.CharacterId] = moved;
        return moved;
    }

    public void Clear() => _characters.Clear();
}
