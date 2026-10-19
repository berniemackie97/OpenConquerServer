using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// A persisted position and its database revision, which only ever increases. The revision isn't a gameplay
/// lock and doesn't prove that a write in progress has finished.
/// </summary>
public sealed class PersistedCharacterLocation
{
    public PersistedCharacterLocation(CharacterLocation location, long revision, Guid? lastOperationId = null, Guid? writerToken = null)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        if (lastOperationId == Guid.Empty)
        {
            throw new ArgumentException("A persisted operation identifier cannot be empty.", nameof(lastOperationId));
        }
        if (writerToken == Guid.Empty)
        {
            throw new ArgumentException("A persisted writer generation cannot be empty.", nameof(writerToken));
        }
        Revision = revision;
        LastOperationId = lastOperationId;
        WriterToken = writerToken;
    }

    public CharacterLocation Location { get; }
    public long Revision { get; }
    public Guid? LastOperationId { get; }
    public Guid? WriterToken { get; }
}
