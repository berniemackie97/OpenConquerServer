using OpenConquer.Infrastructure.Persistence.Game.Characters;

namespace OpenConquer.Infrastructure.Tests.Persistence.Game.Characters;

public sealed class GameCharacterCreationConnectionPolicyTests
{
    private const string Game = "Server=localhost;Port=3306;Database=openconquer_game;User ID=game_reader;Password=read_secret";
    private const string Creation = "Server=localhost;Port=3306;Database=openconquer_game;User ID=game_creator;Password=write_secret";

    [Fact]
    public void Validate_DedicatedIdentityOnSameDatabase_IsAccepted()
    {
        GameCharacterCreationConnectionPolicy.Validate(Game, Creation);
    }

    [Theory]
    [InlineData("Server=localhost;Database=openconquer_game;User ID=game_reader")]
    [InlineData("Server=localhost;Database=openconquer_game;User ID=GAME_READER")]
    [InlineData("Server=localhost;Database=openconquer_game;User ID=root")]
    [InlineData("Server=localhost;Database=openconquer_game")]
    public void Validate_NonDedicatedIdentity_IsRejected(string creationConnectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            GameCharacterCreationConnectionPolicy.Validate(Game, creationConnectionString));
    }

    [Theory]
    [InlineData("Server=other.example;Database=openconquer_game;User ID=game_creator")]
    [InlineData("Server=localhost;Port=3307;Database=openconquer_game;User ID=game_creator")]
    [InlineData("Server=localhost;Database=another_game;User ID=game_creator")]
    [InlineData("Server=localhost;User ID=game_creator")]
    public void Validate_DifferentDatabaseEndpoint_IsRejected(string creationConnectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            GameCharacterCreationConnectionPolicy.Validate(Game, creationConnectionString));
    }

    [Fact]
    public void Validate_InvalidConnectionString_DoesNotDiscloseCredential()
    {
        const string invalid = "Server=localhost;Database=openconquer_game;Password=private_value;DefinitelyUnsupportedOption=1";

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            GameCharacterCreationConnectionPolicy.Validate(Game, invalid));

        Assert.DoesNotContain("private_value", failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void Validate_EmptyConnectionString_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => GameCharacterCreationConnectionPolicy.Validate(Game, " "));
        Assert.Throws<ArgumentException>(() => GameCharacterCreationConnectionPolicy.Validate(" ", Creation));
    }
}
