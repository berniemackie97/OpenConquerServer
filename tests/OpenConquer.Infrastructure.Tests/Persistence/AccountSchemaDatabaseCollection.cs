namespace OpenConquer.Infrastructure.Tests.Persistence;

[CollectionDefinition(Name)]
public sealed class AccountSchemaDatabaseCollection : ICollectionFixture<AccountDatabaseFixture>
{
    public const string Name = "Account schema database";
}
