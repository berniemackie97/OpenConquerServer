namespace OpenConquer.Infrastructure.Tests.Persistence;

[CollectionDefinition(Name)]
public sealed class GameSchemaDatabaseCollection : ICollectionFixture<GameDatabaseFixture>
{
    public const string Name = "Game schema database";
}
