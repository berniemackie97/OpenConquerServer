using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class AccountAuthenticationTypesTests
{
    private const ulong AccountStateRevision = 1;
    private const ulong PasswordCredentialRevision = 1;

    [Fact]
    public void Snapshot_RejectsZeroAccountId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 0,
                "Bernie",
                "$hash$",
                AccountLoginAccess.Allowed,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Snapshot_RejectsMissingPasswordHash(string passwordHash)
    {
        Assert.Throws<ArgumentException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 1,
                "Bernie",
                passwordHash,
                AccountLoginAccess.Allowed,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );
    }

    [Fact]
    public void Snapshot_RejectsUndefinedLoginAccess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 1,
                "Bernie",
                "$hash$",
                (AccountLoginAccess)99,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );
    }

    [Fact]
    public void Snapshot_RejectsZeroAccountStateRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 1,
                "Bernie",
                "$hash$",
                AccountLoginAccess.Allowed,
                accountStateRevision: 0,
                PasswordCredentialRevision
            )
        );
    }

    [Fact]
    public void Snapshot_RejectsZeroPasswordCredentialRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountAuthenticationSnapshot(
                accountId: 1,
                "Bernie",
                "$hash$",
                AccountLoginAccess.Allowed,
                AccountStateRevision,
                passwordCredentialRevision: 0
            )
        );
    }

    [Fact]
    public void SuccessfulResult_RejectsZeroAccountId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccountAuthenticationResult.Succeeded(
                accountId: 0,
                "Bernie",
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );
    }

    [Fact]
    public void SuccessfulResult_RejectsZeroAccountStateRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccountAuthenticationResult.Succeeded(
                accountId: 1,
                "Bernie",
                accountStateRevision: 0,
                PasswordCredentialRevision
            )
        );
    }

    [Fact]
    public void SuccessfulResult_RejectsZeroPasswordCredentialRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccountAuthenticationResult.Succeeded(
                accountId: 1,
                "Bernie",
                AccountStateRevision,
                passwordCredentialRevision: 0
            )
        );
    }

    [Fact]
    public void SuccessfulResult_ExposesAuthoritativeIdentityAndRevisions()
    {
        AccountAuthenticationResult result = AccountAuthenticationResult.Succeeded(
            accountId: 42,
            "Bernie",
            accountStateRevision: 7,
            passwordCredentialRevision: 11
        );

        Assert.Equal(AccountAuthenticationStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(42u, result.AccountId);
        Assert.Equal("Bernie", result.Username);
        Assert.Equal(7ul, result.AccountStateRevision);
        Assert.Equal(11ul, result.PasswordCredentialRevision);
    }

    [Fact]
    public void FailureResults_DoNotExposeAccountIdentityOrRevisions()
    {
        AccountAuthenticationResult invalidCredentials =
            AccountAuthenticationResult.InvalidCredentials();

        AccountAuthenticationResult banned = AccountAuthenticationResult.Banned();

        Assert.Equal(AccountAuthenticationStatus.InvalidCredentials, invalidCredentials.Status);
        Assert.False(invalidCredentials.IsSuccess);
        Assert.Equal(0u, invalidCredentials.AccountId);
        Assert.Null(invalidCredentials.Username);
        Assert.Equal(0ul, invalidCredentials.AccountStateRevision);
        Assert.Equal(0ul, invalidCredentials.PasswordCredentialRevision);

        Assert.Equal(AccountAuthenticationStatus.Banned, banned.Status);
        Assert.False(banned.IsSuccess);
        Assert.Equal(0u, banned.AccountId);
        Assert.Null(banned.Username);
        Assert.Equal(0ul, banned.AccountStateRevision);
        Assert.Equal(0ul, banned.PasswordCredentialRevision);

        AccountAuthenticationResult defaultResult = default(AccountAuthenticationResult);

        Assert.False(defaultResult.IsSuccess);
        Assert.Null(defaultResult.Username);
        Assert.Equal(0ul, defaultResult.AccountStateRevision);
        Assert.Equal(0ul, defaultResult.PasswordCredentialRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AccountIdentity_RequiresCanonicalUsername(string? username)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new AccountAuthenticationSnapshot(
                1,
                username!,
                "$hash$",
                AccountLoginAccess.Allowed,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );

        Assert.ThrowsAny<ArgumentException>(() =>
            AccountAuthenticationResult.Succeeded(
                1,
                username!,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );
    }
}
