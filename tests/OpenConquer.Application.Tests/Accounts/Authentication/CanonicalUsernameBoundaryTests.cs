using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin;

namespace OpenConquer.Application.Tests.Accounts.Authentication;

public sealed class CanonicalUsernameBoundaryTests
{
    private const uint AccountId = 42;
    private const uint SessionUid = 0x1020_3040u;
    private const uint AuthenticationKey = 0x5060_7080u;
    private const ulong AccountStateRevision = 7;
    private const ulong PasswordCredentialRevision = 11;
    private const string PasswordHash = "$openconquer$current$";

    private static readonly DateTimeOffset s_issuedAtUtc = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset s_expiresAtUtc = s_issuedAtUtc.AddMinutes(5);

    [Fact]
    public void AuthenticationSnapshot_NoncanonicalUsernameIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new AccountAuthenticationSnapshot(
                AccountId,
                " Bernie ",
                PasswordHash,
                AccountLoginAccess.Allowed,
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );

        Assert.Equal("username", exception.ParamName);
    }

    [Fact]
    public void AuthenticationResult_NoncanonicalUsernameIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            AccountAuthenticationResult.Succeeded(
                AccountId,
                " Bernie ",
                AccountStateRevision,
                PasswordCredentialRevision
            )
        );

        Assert.Equal("username", exception.ParamName);
    }

    [Fact]
    public void GameLoginTicket_NoncanonicalUsernameIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new GameLoginTicket(
                AccountId,
                " Bernie ",
                SessionUid,
                AuthenticationKey,
                s_issuedAtUtc,
                s_expiresAtUtc
            )
        );

        Assert.Equal("username", exception.ParamName);
    }

    [Fact]
    public void GameLoginTicketIdentity_NoncanonicalUsernameIsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new GameLoginTicketIdentity(AccountId, " Bernie ", SessionUid)
        );

        Assert.Equal("username", exception.ParamName);
    }
}
