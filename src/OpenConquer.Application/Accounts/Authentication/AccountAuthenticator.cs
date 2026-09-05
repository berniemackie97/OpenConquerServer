using System.Net;
using OpenConquer.Domain.Accounts;

namespace OpenConquer.Application.Accounts.Authentication;

/// <summary>
/// Implements authoritative account password authentication.
/// </summary>
public sealed class AccountAuthenticator(IAccountAuthenticationRepository repository, IAccountPasswordHasher passwordHasher, IAccountAuthenticationAttemptLimiter attemptLimiter, TimeProvider timeProvider)
    : IAccountAuthenticator
{
    private const int MaximumPersistenceAttempts = 2;

    private readonly IAccountAuthenticationRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IAccountPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    private readonly IAccountAuthenticationAttemptLimiter _attemptLimiter = attemptLimiter ?? throw new ArgumentNullException(nameof(attemptLimiter));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<AccountAuthenticationResult> AuthenticateAsync(string accountName, ReadOnlyMemory<char> password, IPAddress remoteAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(remoteAddress);

        cancellationToken.ThrowIfCancellationRequested();

        if (!AccountCredentialPolicy.TryNormalizeUsername(accountName, out string username) || !AccountCredentialPolicy.IsValidPassword(password.Span))
        {
            return AccountAuthenticationResult.InvalidCredentials();
        }

        AccountAuthenticationSnapshot? account = await _repository.FindByNameAsync(username, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (account is null)
        {
            _passwordHasher.VerifyDecoy(password.Span);

            cancellationToken.ThrowIfCancellationRequested();

            return AccountAuthenticationResult.InvalidCredentials();
        }

        if (!_attemptLimiter.TryBeginAuthentication(remoteAddress, account.AccountId, out IAccountAuthenticationAttemptLease? authenticationAttempt))
        {
            cancellationToken.ThrowIfCancellationRequested();

            return AccountAuthenticationResult.InvalidCredentials();
        }

        using (authenticationAttempt)
        {
            for (int attempt = 0; attempt < MaximumPersistenceAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AccountPasswordVerificationStatus verificationStatus = _passwordHasher.VerifyPassword(account.PasswordHash, password.Span);

                cancellationToken.ThrowIfCancellationRequested();

                if (verificationStatus == AccountPasswordVerificationStatus.Failed)
                {
                    authenticationAttempt.Complete(credentialsAccepted: false);

                    return AccountAuthenticationResult.InvalidCredentials();
                }

                if (verificationStatus is not (AccountPasswordVerificationStatus.Success or AccountPasswordVerificationStatus.SuccessRehashNeeded))
                {
                    throw new InvalidOperationException($"Password verifier returned unsupported status {verificationStatus}.");
                }

                switch (account.Access)
                {
                    case AccountLoginAccess.Banned:
                        authenticationAttempt.Complete(credentialsAccepted: true);

                        return AccountAuthenticationResult.Banned();

                    case AccountLoginAccess.Denied:
                        authenticationAttempt.Complete(credentialsAccepted: true);

                        return AccountAuthenticationResult.InvalidCredentials();

                    case AccountLoginAccess.Allowed:
                        break;

                    default:
                        throw new InvalidOperationException($"Account authentication snapshot contains unsupported login access {account.Access}.");
                }

                string? replacementPasswordHash = null;
                if (verificationStatus == AccountPasswordVerificationStatus.SuccessRehashNeeded)
                {
                    replacementPasswordHash = _passwordHasher.HashPassword(password.Span);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(replacementPasswordHash))
                    {
                        throw new InvalidOperationException("Password verifier returned an invalid replacement hash.");
                    }
                }

                uint timestamp = checked((uint)_timeProvider.GetUtcNow().ToUnixTimeSeconds());
                bool recorded = await _repository.TryRecordLoginAsync(account, replacementPasswordHash, timestamp, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (recorded)
                {
                    authenticationAttempt.Complete(credentialsAccepted: true);
                    return AccountAuthenticationResult.Succeeded(account.AccountId, account.Username);
                }

                if (attempt == MaximumPersistenceAttempts - 1)
                {
                    break;
                }

                AccountAuthenticationSnapshot? refreshed = await _repository.FindByNameAsync(username, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (refreshed is null || refreshed.AccountId != account.AccountId)
                {
                    _passwordHasher.VerifyDecoy(password.Span);
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
                }

                account = refreshed;
            }

            authenticationAttempt.Complete(credentialsAccepted: false);
            return AccountAuthenticationResult.InvalidCredentials();
        }
    }
}
