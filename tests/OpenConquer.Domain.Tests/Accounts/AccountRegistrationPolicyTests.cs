using OpenConquer.Domain.Accounts;

namespace OpenConquer.Domain.Tests.Accounts;

public sealed class AccountRegistrationPolicyTests
{
    [Theory]
    [InlineData("abcd")]
    [InlineData("Player_01")]
    [InlineData("0123456789abcdef")]
    [InlineData("!~#$")]
    public void IsValidUsername_AcceptsPrintableAsciiWithinLimits(string username)
    {
        Assert.True(AccountRegistrationPolicy.IsValidUsername(username));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789abcdefg")]
    [InlineData(" abcd")]
    [InlineData("abcd ")]
    [InlineData("ab cd")]
    [InlineData("abc\t")]
    [InlineData("abcé")]
    [InlineData("abc\u0000")]
    public void IsValidUsername_RejectsSpacesControlAndNonAsciiOrBadLength(string? username)
    {
        Assert.False(AccountRegistrationPolicy.IsValidUsername(username));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("123456", true)]
    [InlineData("Ab09@.>-_", true)]
    [InlineData("abc de", false)]
    [InlineData("abcdef!", false)]
    [InlineData("abcdefé", false)]
    [InlineData("abcdef\0", false)]
    [InlineData("123456789012345", true)]
    [InlineData("1234567890123456", false)]
    public void IsValidPassword_EnforcesClientFieldBounds(string password, bool expected)
    {
        Assert.Equal(expected, AccountRegistrationPolicy.IsValidPassword(password));
    }
}
