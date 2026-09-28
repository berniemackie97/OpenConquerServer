namespace OpenConquer.Domain.Characters;

public static class CharacterProgressionPolicy
{
    public const byte MinimumLevel = 1;
    public const byte MaximumLevel = 140;

    public static bool IsValidLevel(byte level)
    {
        return level is >= MinimumLevel and <= MaximumLevel;
    }
}
