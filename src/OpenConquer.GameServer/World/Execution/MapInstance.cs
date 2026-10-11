using OpenConquer.Application.World;
using OpenConquer.Domain.Characters;

namespace OpenConquer.GameServer.World.Execution;

/// <summary>
/// Owns mutable map-local character state. Access is restricted to exclusive partition execution turns.
/// </summary>
internal sealed class MapInstance(WorldMapContent map)
{
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

    public void Clear() => _characters.Clear();
}
