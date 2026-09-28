namespace OpenConquer.Infrastructure.Persistence.Game.Items;

public sealed class CharacterItemHydrationOptions
{
    public const int DefaultMaximumItemsPerCharacter = 4_096;
    public const int MaximumSupportedItemsPerCharacter = 65_535;

    public CharacterItemHydrationOptions(int maximumItemsPerCharacter = DefaultMaximumItemsPerCharacter)
    {
        if (maximumItemsPerCharacter < 1 || maximumItemsPerCharacter > MaximumSupportedItemsPerCharacter)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItemsPerCharacter), $"The character item hydration limit must be between 1 and {MaximumSupportedItemsPerCharacter}.");
        }

        MaximumItemsPerCharacter = maximumItemsPerCharacter;
    }

    public int MaximumItemsPerCharacter { get; }
}
