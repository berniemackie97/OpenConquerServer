using OpenConquer.Infrastructure.Persistence.Game.Skills;

namespace OpenConquer.Infrastructure.Tests.Persistence;

public sealed class CharacterMagicHydrationOptionsTests
{
    [Fact]
    public void Constructor_DefaultsMatchProductionPolicy()
    {
        CharacterMagicHydrationOptions options = new();

        Assert.Equal(CharacterMagicHydrationOptions.DefaultMaximumMagicEntriesPerCharacter, options.MaximumMagicEntriesPerCharacter);
        Assert.Equal(4_096, options.MaximumMagicEntriesPerCharacter);
    }

    [Fact]
    public void Constructor_CustomValueIsPreserved()
    {
        CharacterMagicHydrationOptions options = new(maximumMagicEntriesPerCharacter: 512);

        Assert.Equal(512, options.MaximumMagicEntriesPerCharacter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CharacterMagicHydrationOptions.MaximumSupportedMagicEntriesPerCharacter + 1)]
    public void Constructor_InvalidLimitThrows(int value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterMagicHydrationOptions(value));

        Assert.Equal("maximumMagicEntriesPerCharacter", exception.ParamName);
    }

    [Fact]
    public void Constructor_MaximumSupportedLimitIsAccepted()
    {
        CharacterMagicHydrationOptions options = new(CharacterMagicHydrationOptions.MaximumSupportedMagicEntriesPerCharacter);

        Assert.Equal(CharacterMagicHydrationOptions.MaximumSupportedMagicEntriesPerCharacter, options.MaximumMagicEntriesPerCharacter);
        Assert.Equal(65_536, options.MaximumMagicEntriesPerCharacter);
    }
}
