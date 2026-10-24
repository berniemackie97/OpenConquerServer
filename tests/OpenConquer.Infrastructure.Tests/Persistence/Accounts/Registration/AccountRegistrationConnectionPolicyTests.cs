using OpenConquer.Infrastructure.Persistence.Accounts.Registration;

namespace OpenConquer.Infrastructure.Tests.Persistence.Accounts.Registration;

public sealed class AccountRegistrationConnectionPolicyTests
{
    [Fact]
    public void ConnectionPolicy_RequiresSeparateNonRootIdentityOnTheSameEndpoint()
    {
        const string accounts = "Server=db;Port=3306;Database=openconquer_accounts;User ID=runtime;Password=x";

        AccountRegistrationConnectionPolicy.Validate(accounts, "Server=db;Port=3306;Database=openconquer_accounts;User ID=registration;Password=y");

        Assert.Throws<InvalidOperationException>(() => AccountRegistrationConnectionPolicy.Validate(accounts, "Server=db;Port=3306;Database=openconquer_accounts;User ID=runtime;Password=y"));
        Assert.Throws<InvalidOperationException>(() => AccountRegistrationConnectionPolicy.Validate(accounts, "Server=db;Port=3306;Database=openconquer_accounts;User ID=root;Password=y"));
        Assert.Throws<InvalidOperationException>(() => AccountRegistrationConnectionPolicy.Validate(accounts, "Server=other;Port=3306;Database=openconquer_accounts;User ID=registration;Password=y"));
        Assert.Throws<InvalidOperationException>(() => AccountRegistrationConnectionPolicy.Validate(accounts, "Server=db;Port=3306;Database=other;User ID=registration;Password=y"));
    }
}
