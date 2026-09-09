using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketIssuer(IGameLoginTicketGrantStore grantStore, IGameLoginTicketTokenGenerator tokenGenerator)
{
    private const int MaximumAllocationAttempts = 8;

    private static readonly TimeSpan s_ticketLifetime = TimeSpan.FromMinutes(5);

    private readonly IGameLoginTicketGrantStore _grantStore = grantStore ?? throw new ArgumentNullException(nameof(grantStore));
    private readonly IGameLoginTicketTokenGenerator _tokenGenerator = tokenGenerator ?? throw new ArgumentNullException(nameof(tokenGenerator));

    public async ValueTask<GameLoginTicket?> IssueAsync(AccountAuthenticationResult authentication, CancellationToken cancellationToken = default)
    {
        ValidateAuthentication(authentication);

        cancellationToken.ThrowIfCancellationRequested();

        for (int attempt = 0; attempt < MaximumAllocationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint sessionUid = _tokenGenerator.GenerateSessionUid();

            if (sessionUid == 0)
            {
                throw new InvalidOperationException("The game-login ticket token generator returned a zero session UID.");
            }

            uint authenticationKey = _tokenGenerator.GenerateAuthenticationKey();

            if (authenticationKey == 0)
            {
                throw new InvalidOperationException("The game-login ticket token generator returned a zero authentication key.");
            }

            GameLoginTicketGrantRequest request = new(authentication.AccountId, authentication.Username!, sessionUid, authenticationKey);
            GameLoginTicketGrantResult result = await _grantStore.TryGrantAsync(request, s_ticketLifetime, authentication.AccountStateRevision, authentication.PasswordCredentialRevision, cancellationToken).ConfigureAwait(false);

            switch (result.Status)
            {
                case GameLoginTicketGrantStatus.Granted:
                    return ValidateGrantedTicket(result, request);

                case GameLoginTicketGrantStatus.SessionUidCollision:
                    break;

                case GameLoginTicketGrantStatus.AuthenticationStateChanged:
                    return null;

                default:
                    throw new InvalidOperationException($"Game-login ticket persistence returned unsupported grant status {result.Status}.");
            }
        }

        throw new GameLoginTicketAllocationException();
    }

    private static GameLoginTicket ValidateGrantedTicket(GameLoginTicketGrantResult result, GameLoginTicketGrantRequest request)
    {
        GameLoginTicket ticket = result.Ticket ?? throw new InvalidOperationException("Game-login ticket persistence reported a successful grant without returning the durable ticket.");

        if (ticket.AccountId != request.AccountId || !string.Equals(ticket.Username, request.Username, StringComparison.Ordinal)
                                                  || ticket.SessionUid != request.SessionUid || ticket.AuthenticationKey != request.AuthenticationKey)
        {
            throw new InvalidOperationException("Game-login ticket persistence returned a durable ticket that does not match the requested grant.");
        }

        if (ticket.ExpiresAtUtc - ticket.IssuedAtUtc != s_ticketLifetime)
        {
            throw new InvalidOperationException("Game-login ticket persistence returned a durable ticket with an unexpected lifetime.");
        }

        return ticket;
    }

    private static void ValidateAuthentication(AccountAuthenticationResult authentication)
    {
        if (!authentication.IsSuccess)
        {
            throw new ArgumentException("A game-login ticket can only be issued from a successful authentication result.", nameof(authentication));
        }

        if (authentication.AccountId == 0)
        {
            throw new ArgumentException("The successful authentication result does not identify a persisted account.", nameof(authentication));
        }

        if (string.IsNullOrWhiteSpace(authentication.Username))
        {
            throw new ArgumentException("The successful authentication result does not contain a canonical username.", nameof(authentication));
        }

        if (authentication.AccountStateRevision == 0)
        {
            throw new ArgumentException("The successful authentication result does not contain a valid account-state revision.", nameof(authentication));
        }

        if (authentication.PasswordCredentialRevision == 0)
        {
            throw new ArgumentException("The successful authentication result does not contain a valid password-credential revision.", nameof(authentication));
        }
    }
}
