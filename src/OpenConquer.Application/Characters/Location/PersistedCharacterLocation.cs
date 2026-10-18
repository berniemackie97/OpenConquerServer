using OpenConquer.Application.Characters.Login.Profile;

namespace OpenConquer.Application.Characters.Location;

/// <summary>
/// A persisted position and its database revision, which only ever increases. The revision isn't a gameplay
/// lock and doesn't prove that a write in progress has finished.
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
