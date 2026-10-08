using OpenConquer.Domain.World;

namespace OpenConquer.Domain.Tests.World;

public sealed class MapDefinitionTests
{
    [Fact]
    public void Constructor_ValidIdentity_PreservesCompleteFlags()
    {
        const ulong flags = 0x0000_0040_0000_0010;

        MapDefinition definition = new(1002, 1010, flags);

        Assert.Equal(1002u, definition.MapId);
        Assert.Equal(1010u, definition.MapDataId);
        Assert.Equal(flags, definition.Flags);
    }

    [Fact]
    public void Constructor_ZeroFlags_AreAllowed()
    {
        MapDefinition definition = new(1002, 1010, 0);

        Assert.Equal(0ul, definition.Flags);
    }

    [Fact]
    public void Constructor_MaximumFlags_ArePreserved()
    {
        MapDefinition definition = new(1002, 1010, ulong.MaxValue);

        Assert.Equal(ulong.MaxValue, definition.Flags);
    }

    [Fact]
    public void Constructor_ZeroMapId_IsRejected()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapDefinition(0, 1010, 0));

        Assert.Equal("mapId", exception.ParamName);
    }

    [Fact]
    public void Constructor_ZeroMapDataId_IsRejected()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MapDefinition(1002, 0, 0));

        Assert.Equal("mapDataId", exception.ParamName);
    }

    [Fact]
    public void Equality_UsesCompleteIdentityAndFlags()
    {
        MapDefinition first = new(1002, 1010, 0x0000_0040_0000_0000);
        MapDefinition equivalent = new(1002, 1010, 0x0000_0040_0000_0000);
        MapDefinition differentFlags = new(1002, 1010, 0);

        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, differentFlags);
    }
}
