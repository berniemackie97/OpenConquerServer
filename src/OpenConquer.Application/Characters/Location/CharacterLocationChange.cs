using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// An authorized caller's proposed durable location transition, with an expected persisted
/// position. This value does not itself grant world or database ownership.
/// </summary>
public sealed class CharacterLocationChange
{
    public CharacterLocationChange(uint accountId, uint characterId, CharacterLocation expected, CharacterLocation destination)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }
        if (!CharacterIdentityPolicy.IsPlayerEntityId(characterId))
        {
            throw new ArgumentOutOfRangeException(nameof(characterId));
        }
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(destination);
        if (expected.MapId == destination.MapId && expected.X == destination.X && expected.Y == destination.Y)
        {
            throw new ArgumentException("A durable location transition must change at least one coordinate or map identity.", nameof(destination));
        }

        AccountId = accountId;
        CharacterId = characterId;
        Expected = expected;
        Destination = destination;
    }

    public uint AccountId { get; }
    public uint CharacterId { get; }
    public CharacterLocation Expected { get; }
    public CharacterLocation Destination { get; }
}
