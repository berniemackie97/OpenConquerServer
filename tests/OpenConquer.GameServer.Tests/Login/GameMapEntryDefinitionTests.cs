using OpenConquer.Domain.World;
using OpenConquer.GameServer.Login.WorldEntry;

namespace OpenConquer.GameServer.Tests.Login;

public sealed class GameMapEntryDefinitionTests
{
    [Fact]
    public void Constructor_CanonicalDefinition_PreservesCompleteMapMetadata()
    {
        MapDefinition source = new(1002, 1010, 0x0000_0040_0000_0010);

        GameMapEntryDefinition result = new(source);

        Assert.Equal(source.MapId, result.MapId);
        Assert.Equal(source.MapDataId, result.MapDataId);
        Assert.Equal(source.Flags, result.Flags);
    }

    [Fact]
    public void Constructor_NullCanonicalDefinition_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GameMapEntryDefinition(null!));
    }

    [Fact]
    public void Constructor_ExistingFields_RemainsCompatible()
    {
        GameMapEntryDefinition definition = new(1002, 1010, ulong.MaxValue);

        Assert.Equal(1002u, definition.MapId);
        Assert.Equal(1010u, definition.MapDataId);
        Assert.Equal(ulong.MaxValue, definition.Flags);
    }
}
