using OpenConquer.Infrastructure.Persistence.Game.Items;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class CharacterItemHydrationOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        CharacterItemHydrationOptions options = new();

        Assert.Equal(CharacterItemHydrationOptions.DefaultMaximumItemsPerCharacter, options.MaximumItemsPerCharacter);
        Assert.Equal(4_096, options.MaximumItemsPerCharacter);
    }

    [Fact]
    public void Constructor_CustomValueIsPreserved()
    {
        CharacterItemHydrationOptions options = new(maximumItemsPerCharacter: 512);

        Assert.Equal(512, options.MaximumItemsPerCharacter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CharacterItemHydrationOptions.MaximumSupportedItemsPerCharacter + 1)]
    public void Constructor_InvalidLimitThrows(int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterItemHydrationOptions(value));

        Assert.Equal("maximumItemsPerCharacter", exception.ParamName);
    }

    [Fact]
    public void Constructor_MaximumSupportedLimitIsAccepted()
    {
        CharacterItemHydrationOptions options = new(CharacterItemHydrationOptions.MaximumSupportedItemsPerCharacter);

        Assert.Equal(CharacterItemHydrationOptions.MaximumSupportedItemsPerCharacter, options.MaximumItemsPerCharacter);
    }
}
