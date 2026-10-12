using OpenConquer.Application.Characters.Creation;
using OpenConquer.Application.World;

namespace OpenConquer.GameServer.Configuration;

/// <summary>
/// Verifies a configured creation spawn against the canonical world snapshot before publication.
/// Does not authorize character creation or interpret unproven passability flags.
/// </summary>
internal sealed class GameServerCharacterCreationReadiness(CharacterCreationStartingState? startingState)
{
    private readonly CharacterCreationStartingState? _startingState = startingState;

    public void Validate(WorldMapContentCatalog maps)
    {
        ArgumentNullException.ThrowIfNull(maps);

        if (_startingState is null)
        {
            return;
        }

        if (!maps.TryGet(_startingState.MapId, out WorldMapContent? map))
        {
            throw new InvalidDataException($"Character creation starting map {_startingState.MapId} is absent from the canonical world.");
        }

        if (_startingState.PositionX >= map.Collision.Width || _startingState.PositionY >= map.Collision.Height)
        {
            throw new InvalidDataException(
                $"Character creation starting position ({_startingState.PositionX}, {_startingState.PositionY}) is outside map {_startingState.MapId} ({map.Collision.Width} x {map.Collision.Height}).");
        }
    }
}
