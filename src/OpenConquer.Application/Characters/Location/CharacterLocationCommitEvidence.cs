namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// Checks positive evidence for one committed, revision-qualified location operation.
/// A false result says nothing about an uncompleted database command and cannot authorize retry.
/// </summary>
public static class CharacterLocationCommitEvidence
{
    public static bool IsConfirmed(CharacterLocationChange change, PersistedCharacterLocation? observed)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (observed is null || observed.LastOperationId != change.OperationId ||
            observed.Revision != change.ExpectedRevision + 1)
        {
            return false;
        }

        return observed.Location.MapId == change.Destination.MapId &&
               observed.Location.X == change.Destination.X &&
               observed.Location.Y == change.Destination.Y;
    }
}
