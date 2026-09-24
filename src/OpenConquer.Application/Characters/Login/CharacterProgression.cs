using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Login;

public sealed class CharacterProgression
{
    public CharacterProgression(byte level, ulong experience, byte profession, byte firstProfession, byte previousProfession, byte rebirthCount, byte preRebirthLevel)
    {
        if (!CharacterProgressionPolicy.IsValidLevel(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), $"A persisted character level must be between {CharacterProgressionPolicy.MinimumLevel} and {CharacterProgressionPolicy.MaximumLevel}.");
        }

        if (profession == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profession), "A persisted character requires a nonzero profession.");
        }

        if (preRebirthLevel != 0 && !CharacterProgressionPolicy.IsValidLevel(preRebirthLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(preRebirthLevel), $"A retained pre-rebirth level must be zero or between {CharacterProgressionPolicy.MinimumLevel} and {CharacterProgressionPolicy.MaximumLevel}.");
        }

        if (rebirthCount == 0 && preRebirthLevel != 0)
        {
            throw new ArgumentException("A character that has never been reborn cannot have a retained pre-rebirth level.", nameof(preRebirthLevel));
        }

        if (rebirthCount != 0 && preRebirthLevel == 0)
        {
            throw new ArgumentException("A reborn character requires a retained pre-rebirth level.", nameof(preRebirthLevel));
        }

        Level = level;
        Experience = experience;
        Profession = profession;
        FirstProfession = firstProfession;
        PreviousProfession = previousProfession;
        RebirthCount = rebirthCount;
        PreRebirthLevel = preRebirthLevel;
    }

    public byte Level { get; }
    public ulong Experience { get; }
    public byte Profession { get; }
    public byte FirstProfession { get; }
    public byte PreviousProfession { get; }
    public byte RebirthCount { get; }
    public byte PreRebirthLevel { get; }
}
