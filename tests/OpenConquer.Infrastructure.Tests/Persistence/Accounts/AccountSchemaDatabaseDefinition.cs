namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts;

[CollectionDefinition(Name)]
public sealed class AccountSchemaDatabaseDefinition : ICollectionFixture<AccountDatabaseFixture>
{
    public const string Name = "Account schema database";
}
