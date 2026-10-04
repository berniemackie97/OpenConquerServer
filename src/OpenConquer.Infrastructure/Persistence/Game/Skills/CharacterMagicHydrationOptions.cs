namespace OpenConquer.Infrastructure.Persistence.Game.Skills;

public sealed class CharacterMagicHydrationOptions
{
    public const int DefaultMaximumMagicEntriesPerCharacter = 4_096;
    public const int MaximumSupportedMagicEntriesPerCharacter = ushort.MaxValue + 1;

    public CharacterMagicHydrationOptions(int maximumMagicEntriesPerCharacter = DefaultMaximumMagicEntriesPerCharacter)
    {
        if (maximumMagicEntriesPerCharacter < 1 || maximumMagicEntriesPerCharacter > MaximumSupportedMagicEntriesPerCharacter)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMagicEntriesPerCharacter), $"The character magic hydration limit must be between 1 and {MaximumSupportedMagicEntriesPerCharacter}.");
        }

        MaximumMagicEntriesPerCharacter = maximumMagicEntriesPerCharacter;
    }

    public int MaximumMagicEntriesPerCharacter { get; }
}
