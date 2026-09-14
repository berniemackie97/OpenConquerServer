using OpenConquer.Domain.Characters;

namespace OpenConquer.Application.Characters.Login;

public sealed class CharacterProgression
{
    public CharacterProgression(byte level, ulong experience, byte profession, byte firstProfession, byte previousProfession, byte rebirthCount)
    {
        if (!CharacterProgressionPolicy.IsValidLevel(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), $"A persisted character level must be between {CharacterProgressionPolicy.MinimumLevel} and {CharacterProgressionPolicy.MaximumLevel}.");
        }

        if (profession == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profession), "A persisted character requires a nonzero profession.");
        }

        Level = level;
        Experience = experience;
        Profession = profession;
        FirstProfession = firstProfession;
        PreviousProfession = previousProfession;
        RebirthCount = rebirthCount;
    }

    public byte Level { get; }
    public ulong Experience { get; }
    public byte Profession { get; }
    public byte FirstProfession { get; }
    public byte PreviousProfession { get; }
    public byte RebirthCount { get; }
}
