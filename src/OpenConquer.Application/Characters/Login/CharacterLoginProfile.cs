namespace OpenConquer.Application.Characters.Login;

public sealed class CharacterLoginProfile(CharacterLoginIdentity identity, CharacterAppearance appearance, CharacterProgression progression,
    CharacterAttributes attributes, CharacterVitals vitals, CharacterEconomy economy, short pkPoints, ushort titleId,
    ushort enlightenmentPoints, CharacterLocation location)
{
    public CharacterLoginIdentity Identity { get; } = identity ?? throw new ArgumentNullException(nameof(identity));
    public CharacterAppearance Appearance { get; } = appearance ?? throw new ArgumentNullException(nameof(appearance));
    public CharacterProgression Progression { get; } = progression ?? throw new ArgumentNullException(nameof(progression));
    public CharacterAttributes Attributes { get; } = attributes;
    public CharacterVitals Vitals { get; } = vitals;
    public CharacterEconomy Economy { get; } = economy;
    public short PkPoints { get; } = pkPoints;
    public ushort TitleId { get; } = titleId;
    public ushort EnlightenmentPoints { get; } = enlightenmentPoints;
    public CharacterLocation Location { get; } = location ?? throw new ArgumentNullException(nameof(location));
}
