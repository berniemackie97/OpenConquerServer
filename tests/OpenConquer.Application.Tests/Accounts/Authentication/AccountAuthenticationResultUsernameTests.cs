using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class AccountAuthenticationResultUsernameTests
{
    private const uint AccountId = 42;
    private const ulong AccountStateRevision = 7;
    private const ulong PasswordCredentialRevision = 11;

    [Fact]
    public void Succeeded_MaximumLengthUsernameIsAccepted()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength);

        AccountAuthenticationResult result = AccountAuthenticationResult.Succeeded(
            AccountId,
            username,
            AccountStateRevision,
            PasswordCredentialRevision
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(username, result.Username);
    }

    [Fact]
    public void Succeeded_UsernameLongerThanMaximumIsRejected()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AccountAuthenticationResult.Succeeded(
                AccountId,
                username,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );

        Assert.Equal("username", exception.ParamName);
    }
}
