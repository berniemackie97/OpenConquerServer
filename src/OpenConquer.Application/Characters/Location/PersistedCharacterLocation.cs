using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// A persisted coordinate and its monotonic database revision. The revision is not
/// a gameplay ownership token or proof that an in-flight write has finished.
/// </summary>
public sealed class PersistedCharacterLocation
{
    public PersistedCharacterLocation(CharacterLocation location, long revision)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Revision = revision;
    }

    public CharacterLocation Location { get; }
    public long Revision { get; }
}
