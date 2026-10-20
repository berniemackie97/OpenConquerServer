using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game.Characters;

public sealed class GameCharacterLocationWriteConnectionPolicyTests
{
    private const string Readonly = "Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=oc_game_runtime;Password=readonly";
    private const string Writer = "Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=oc_location_writer;Password=writer";

    [Fact]
    public void Validate_DedicatedIdentityOnSameRealmEndpointSucceeds()
    {
        GameCharacterLocationWriteConnectionPolicy.Validate(Readonly, Writer);
    }

    [Theory]
    [InlineData("Server=127.0.0.1;Port=3308;Database=other;User ID=oc_location_writer;Password=writer")]
    [InlineData("Server=127.0.0.1;Port=3309;Database=openconquer_game;User ID=oc_location_writer;Password=writer")]
    [InlineData("Server=another;Port=3308;Database=openconquer_game;User ID=oc_location_writer;Password=writer")]
    [InlineData("Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=root;Password=writer")]
    [InlineData("Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=oc_game_runtime;Password=writer")]
    [InlineData("Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=oc_location_writer")]
    public void Validate_RejectsWrongEndpointOrUnsafeIdentity(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => GameCharacterLocationWriteConnectionPolicy.Validate(Readonly, connectionString));
    }

    [Fact]
    public void Validate_RejectsReusedCreationIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => GameCharacterLocationWriteConnectionPolicy.Validate(
            Readonly, Writer, "Server=127.0.0.1;Port=3308;Database=openconquer_game;User ID=oc_location_writer;Password=creation"));
    }
}
