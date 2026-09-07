using OpenConquer.Application.Accounts.Authentication;

namespace OpenConquer.Application.Accounts.GameLogin;

public sealed class GameLoginTicketIssuer(IGameLoginTicketGrantStore grantStore, IGameLoginTicketTokenGenerator tokenGenerator, TimeProvider timeProvider)
{
    private const int MaximumAllocationAttempts = 8;

    private static readonly TimeSpan s_ticketLifetime = TimeSpan.FromMinutes(5);

    private readonly IGameLoginTicketGrantStore _grantStore = grantStore ?? throw new ArgumentNullException(nameof(grantStore));
    private readonly IGameLoginTicketTokenGenerator _tokenGenerator = tokenGenerator ?? throw new ArgumentNullException(nameof(tokenGenerator));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<GameLoginTicket?> IssueAsync(AccountAuthenticationResult authentication, CancellationToken cancellationToken = default)
    {
        ValidateAuthentication(authentication);

        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset issuedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        DateTimeOffset expiresAtUtc;
        try
        {
            expiresAtUtc = issuedAtUtc.Add(s_ticketLifetime);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException("The current UTC time cannot represent the configured game-login ticket lifetime.", exception);
        }

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

            GameLoginTicket ticket = new(authentication.AccountId, authentication.Username!, sessionUid, authenticationKey, issuedAtUtc, expiresAtUtc);

            GameLoginTicketGrantStatus status = await _grantStore.TryGrantAsync(ticket, authentication.AccountStateRevision, authentication.PasswordCredentialRevision, cancellationToken).ConfigureAwait(false);

            switch (status)
            {
                case GameLoginTicketGrantStatus.Granted:
                    return ticket;

                case GameLoginTicketGrantStatus.SessionUidCollision:
                    break;

                case GameLoginTicketGrantStatus.AuthenticationStateChanged:
                    return null;

                default:
                    throw new InvalidOperationException($"Game-login ticket persistence returned unsupported grant status {status}.");
            }
        }

        throw new GameLoginTicketAllocationException();
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
