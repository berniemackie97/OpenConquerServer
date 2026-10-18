using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// A proposed location change and the persisted position it expects to replace. Holding one grants nothing in
/// the world or the database.
/// </summary>
public sealed class CharacterLocationChange
{
    public CharacterLocationChange(uint accountId, uint characterId, CharacterLocation expected,
        CharacterLocation destination, long expectedRevision, Guid operationId)
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
        if (expectedRevision < 0 || expectedRevision == long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("A location operation requires a nonempty identifier.", nameof(operationId));
        }
        if (expected.MapId == destination.MapId && expected.X == destination.X && expected.Y == destination.Y)
        {
            throw new ArgumentException("A durable location transition must change at least one coordinate or map identity.", nameof(destination));
        }

        AccountId = accountId;
        CharacterId = characterId;
        Expected = expected;
        Destination = destination;
        ExpectedRevision = expectedRevision;
        OperationId = operationId;
    }

    public uint AccountId { get; }
    public uint CharacterId { get; }
    public CharacterLocation Expected { get; }
    public CharacterLocation Destination { get; }
    public long ExpectedRevision { get; }
    public Guid OperationId { get; }
}
