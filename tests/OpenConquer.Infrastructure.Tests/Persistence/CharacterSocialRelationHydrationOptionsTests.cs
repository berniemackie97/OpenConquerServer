using OpenConquer.Infrastructure.Persistence.Game.Social;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class CharacterSocialRelationHydrationOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        CharacterSocialRelationHydrationOptions options = new();

        Assert.Equal(CharacterSocialRelationHydrationOptions.DefaultMaximumRelationsPerCharacter, options.MaximumRelationsPerCharacter);
        Assert.Equal(4_096, options.MaximumRelationsPerCharacter);
    }

    [Fact]
    public void Constructor_CustomValueIsPreserved()
    {
        CharacterSocialRelationHydrationOptions options = new(maximumRelationsPerCharacter: 512);

        Assert.Equal(512, options.MaximumRelationsPerCharacter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CharacterSocialRelationHydrationOptions.MaximumSupportedRelationsPerCharacter + 1)]
    public void Constructor_InvalidLimitThrows(int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterSocialRelationHydrationOptions(value));

        Assert.Equal("maximumRelationsPerCharacter", exception.ParamName);
    }

    [Fact]
    public void Constructor_MaximumSupportedLimitIsAccepted()
    {
        CharacterSocialRelationHydrationOptions options = new(CharacterSocialRelationHydrationOptions.MaximumSupportedRelationsPerCharacter);

        Assert.Equal(CharacterSocialRelationHydrationOptions.MaximumSupportedRelationsPerCharacter, options.MaximumRelationsPerCharacter);
    }
}
