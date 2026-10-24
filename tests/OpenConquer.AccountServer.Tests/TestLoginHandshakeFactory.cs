using OpenConquer.AccountServer.Login.Handshake;
using OpenConquer.AccountServer.Login.Handshake.Authentication;
using OpenConquer.AccountServer.Login.Handshake.Registration;
using OpenConquer.Application.Accounts.Authentication;
using OpenConquer.Application.Accounts.GameLogin.Issuance;
using OpenConquer.Application.Accounts.Registration;
using OpenConquer.Application.Accounts.Sessions;
using OpenConquer.Application.Realms;

namespace OpenConquer.AccountServer.Tests;

internal static class TestLoginHandshakeFactory
{
    public static LoginHandshakeProcessor Create(IAccountAuthenticator authenticator, GameLoginTicketIssuer ticketIssuer,
        IRealmRegistry realmRegistry, IRealmStatusDirectory realmStatus, IAccountSessionKickRequester sessions,
        LoginHandshakeConfiguration configuration, AccountRegistrationService? registration = null)
    {
        return new LoginHandshakeProcessor(new LoginAuthenticationHandler(authenticator, ticketIssuer, realmRegistry, realmStatus, sessions, configuration),
            new AccountRegistrationHandler(registration), configuration);
    }
}
