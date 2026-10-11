namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts;

[CollectionDefinition(Name)]
public sealed class AccountSchemaDatabaseCollection : ICollectionFixture<AccountDatabaseFixture>
{
    public const string Name = "Account schema database";
}
