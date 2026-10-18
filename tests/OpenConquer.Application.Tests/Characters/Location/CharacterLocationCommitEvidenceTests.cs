using OpenConquer.Application.Characters.Location;
using OpenConquer.Application.Characters.Login.Profile;
using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Tests.Characters.Location;

public sealed class CharacterLocationCommitEvidenceTests
{
    [Fact]
    public void ExactOperationAndRevisionAndDestination_ConfirmACommittedWrite()
    {
        Guid operationId = Guid.NewGuid();
        CharacterLocation expected = new(1002, 431, 379);
        CharacterLocation destination = new(1011, 193, 266);
        CharacterLocationChange change = new(42, CharacterIdentityPolicy.FirstPlayerEntityId,
            expected, destination, expectedRevision: 5, operationId: operationId);
        PersistedCharacterLocation receipt = new(destination, 6, operationId);

        Assert.True(CharacterLocationCommitEvidence.IsConfirmed(change, receipt));
    }

    [Fact]
    public void AmbiguousObservationNeverConfirmsOperation()
    {
        Guid operationId = Guid.NewGuid();
        CharacterLocation expected = new(1002, 431, 379);
        CharacterLocation destination = new(1011, 193, 266);
        CharacterLocationChange change = new(42, CharacterIdentityPolicy.FirstPlayerEntityId,
            expected, destination, expectedRevision: 5, operationId: operationId);

        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, null));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, new(destination, 6)));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, new(destination, 6, Guid.NewGuid())));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, new(destination, 5, operationId)));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, new(destination, 7, operationId)));
        Assert.False(CharacterLocationCommitEvidence.IsConfirmed(change, new(expected, 6, operationId)));
        Assert.Throws<ArgumentException>(() => new PersistedCharacterLocation(destination, 6, Guid.Empty));
    }
}
