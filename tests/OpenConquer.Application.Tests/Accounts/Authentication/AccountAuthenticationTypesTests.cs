using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class AccountAuthenticationTypesTests
{
    [Fact]
    public void Snapshot_RejectsZeroAccountId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(accountId: 0, "Bernie", "$hash$", AccountLoginAccess.Allowed)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Snapshot_RejectsMissingPasswordHash(string passwordHash)
    {
        Assert.Throws<ArgumentException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 1, "Bernie",
                passwordHash,
                AccountLoginAccess.Allowed
            )
        );
    }

    [Fact]
    public void Snapshot_RejectsUndefinedLoginAccess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(accountId: 1, "Bernie", "$hash$", (AccountLoginAccess)99)
        );
    }

    [Fact]
    public void SuccessfulResult_RejectsZeroAccountId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccountAuthenticationResult.Succeeded(accountId: 0, "Bernie")
        );
    }

    [Fact]
    public void FailureResults_DoNotExposeAccountIdentity()
    {
        AccountAuthenticationResult invalidCredentials =
            AccountAuthenticationResult.InvalidCredentials();

        AccountAuthenticationResult banned = AccountAuthenticationResult.Banned();

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, invalidCredentials.Status);

        Assert.False(invalidCredentials.IsSuccess);

        Assert.Equal(0u, invalidCredentials.AccountId);

        Assert.Equal(AccountAuthenticationStatus.Banned, banned.Status);

        Assert.False(banned.IsSuccess);

        Assert.Equal(0u, banned.AccountId);

        Assert.Null(invalidCredentials.Username);
        Assert.Null(banned.Username);
        Assert.False(default(AccountAuthenticationResult).IsSuccess);
        Assert.Null(default(AccountAuthenticationResult).Username);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AccountIdentity_RequiresCanonicalUsername(string? username)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AccountAuthenticationSnapshot(1, username!, "$hash$", AccountLoginAccess.Allowed));
        Assert.ThrowsAny<ArgumentException>(() => AccountAuthenticationResult.Succeeded(1, username!));
    }
}
