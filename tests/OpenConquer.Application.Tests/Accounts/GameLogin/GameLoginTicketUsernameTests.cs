using OpenConquer.Application.Accounts.GameLogin;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Tests.Accounts.GameLogin;

public sealed class GameLoginTicketUsernameTests
{
    private const uint AccountId = 42;
    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;

    private static readonly DateTimeOffset s_issuedAtUtc = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset s_expiresAtUtc = s_issuedAtUtc.AddMinutes(5);

    [Fact]
    public void Ticket_MaximumLengthUsernameIsAccepted()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength);

        GameLoginTicket ticket = new(
            AccountId,
            username,
            SessionUid,
            AuthenticationKey,
            s_issuedAtUtc,
            s_expiresAtUtc
        );

        Assert.Equal(username, ticket.Username);
    }

    [Fact]
    public void Ticket_UsernameLongerThanMaximumIsRejected()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new GameLoginTicket(
                AccountId,
                username,
                SessionUid,
                AuthenticationKey,
                s_issuedAtUtc,
                s_expiresAtUtc
            )
        );

        Assert.Equal("username", exception.ParamName);
    }

    [Fact]
    public void Identity_MaximumLengthUsernameIsAccepted()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength);

        GameLoginTicketIdentity identity = new(AccountId, username, SessionUid);

        Assert.Equal(username, identity.Username);
    }

    [Fact]
    public void Identity_UsernameLongerThanMaximumIsRejected()
    {
        string username = new('A', AccountCredentialPolicy.MaximumUsernameLength + 1);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new GameLoginTicketIdentity(AccountId, username, SessionUid)
        );

        Assert.Equal("username", exception.ParamName);
    }
}
