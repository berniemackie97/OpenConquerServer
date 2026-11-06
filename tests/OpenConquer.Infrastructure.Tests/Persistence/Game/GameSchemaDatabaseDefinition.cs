namespace OpenConquer.Infrastructure.Tests.Persistence.Game;

[CollectionDefinition(Name)]
public sealed class GameSchemaDatabaseDefinition : ICollectionFixture<GameDatabaseFixture>
{
    public const string Name = "Game schema database";
}
