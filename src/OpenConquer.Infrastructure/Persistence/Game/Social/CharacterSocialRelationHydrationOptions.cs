namespace OpenConquer.Infrastructure.Persistence.Game.Social;

public sealed class CharacterSocialRelationHydrationOptions
{
    public const int DefaultMaximumRelationsPerCharacter = 4_096;
    public const int MaximumSupportedRelationsPerCharacter = 65_535;

    public CharacterSocialRelationHydrationOptions(int maximumRelationsPerCharacter = DefaultMaximumRelationsPerCharacter)
    {
        if (maximumRelationsPerCharacter < 1 || maximumRelationsPerCharacter > MaximumSupportedRelationsPerCharacter)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRelationsPerCharacter), $"The character social-relation hydration limit must be between 1 and {MaximumSupportedRelationsPerCharacter}.");
        }

        MaximumRelationsPerCharacter = maximumRelationsPerCharacter;
    }

    public int MaximumRelationsPerCharacter { get; }
}
