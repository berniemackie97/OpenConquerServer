using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters.Location;

public sealed class CharacterLocationChangeTests
{
    [Fact]
    public void Constructor_PreservesExplicitCharacterAccountAndLocationCAS()
    {
        CharacterLocation before = new(1002, 431, 379);
        CharacterLocation after = new(1011, 193, 266);
        CharacterLocationChange change = new(17, CharacterIdentityPolicy.FirstPlayerEntityId, before, after, expectedRevision: 7, operationId: Guid.NewGuid());

        Assert.Equal(17u, change.AccountId);
        Assert.Equal(CharacterIdentityPolicy.FirstPlayerEntityId, change.CharacterId);
        Assert.Same(before, change.Expected);
        Assert.Same(after, change.Destination);
        Assert.Equal(7, change.ExpectedRevision);
        Assert.NotEqual(Guid.Empty, change.OperationId);
    }

    [Fact]
    public void Constructor_RejectsInvalidAndNoopTransitions()
    {
        CharacterLocation origin = new(1002, 431, 379);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLocationChange(0, 1_000_000, origin, new CharacterLocation(1011, 1, 1), 0, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLocationChange(17, 999_999, origin, new CharacterLocation(1011, 1, 1), 0, Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() => new CharacterLocationChange(17, 1_000_000, null!, origin, 0, Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() => new CharacterLocationChange(17, 1_000_000, origin, null!, 0, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new CharacterLocationChange(17, 1_000_000, origin, new CharacterLocation(1002, 431, 379), 0, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLocationChange(17, 1_000_000, origin, new CharacterLocation(1011, 1, 1), -1, Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterLocationChange(17, 1_000_000, origin, new CharacterLocation(1011, 1, 1), long.MaxValue, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new CharacterLocationChange(17, 1_000_000, origin,
            new CharacterLocation(1011, 1, 1), 0, Guid.Empty));
    }
}
